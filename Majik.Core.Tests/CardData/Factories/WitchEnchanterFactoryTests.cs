using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="WitchEnchanterFactory"/> and
/// <see cref="WitchBlessedMeadowFactory"/> — the front + back faces of the
/// Wilds of Eldraine modal double-faced card
/// Witch Enchanter // Witch-Blessed Meadow.
///
/// Front face (Witch Enchanter, {3}{W}):
///   Creature — Human Warlock 2/2.
///   "When this creature enters, destroy target artifact or enchantment
///    an opponent controls."
///
/// Back face (Witch-Blessed Meadow):
///   Land. "As this land enters, you may pay 3 life. If you don't, it
///   enters tapped." "{T}: Add {W}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, subtypes, P/T, owner).
/// - <see cref="NamedCardFactory"/> dispatches both printed names.
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front face — single ETB trigger, 1..1 "target artifact or enchantment
///   an opponent controls" request.
/// - Front face — resolve: agent-set opponent artifact → destroyed.
/// - Front face — resolve: agent-set opponent enchantment → destroyed.
/// - Front face — resolve: agent-set own artifact (illegal — not an
///   opponent's) → no destroy (CR 608.2b).
/// - Front face — resolve: creature target (illegal pick) → no destroy.
/// - Front face — resolve: target left the battlefield → no destroy.
/// - Front face — resolve: no agent target + no legal candidate → clean no-op.
/// - Front face — resolve: no agent target + opponent artifact on
///   battlefield → deterministic fallback destroys it.
/// - Back face — {T}: Add {W} mana ability attached.
/// - Back face — pay 3 life → enters untapped.
/// - Back face — decline → enters tapped.
/// - Back face — can't pay (life &lt; 3) → enters tapped (CR 119.4).
/// - Back face — no agent → enters tapped.
/// </summary>
public class WitchEnchanterFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public WitchEnchanterFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void WitchEnchanter_Identity_Creature_HumanWarlock_2_2_At3W()
    {
        var card = WitchEnchanterFactory.Create(_alice);

        card.Name.Should().Be("Witch Enchanter");
        card.ManaCost.Should().Be("{3}{W}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warlock).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WitchEnchanter_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Witch Enchanter", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Witch Enchanter");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{W}");
    }

    [Fact]
    public void WitchEnchanter_HasMdfcTracker_OnFrontFace()
    {
        var card = WitchEnchanterFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull();
        card.MdfcState!.FrontFaceName.Should().Be("Witch Enchanter");
        card.MdfcState.BackFaceName.Should().Be("Witch-Blessed Meadow");
        card.MdfcState.IsBackFace.Should().BeFalse();
        card.MdfcState.ActiveFaceName.Should().Be("Witch Enchanter");
    }

    [Fact]
    public void WitchEnchanter_HasSingleEtbTrigger_WithOneArtifactOrEnchantmentTarget()
    {
        var card = WitchEnchanterFactory.Create(_alice);

        var triggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("artifact").And.Contain("enchantment");

        // ETB lives on the battlefield (CR 603.6a).
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // =========================================================================
    // Front face — resolution
    // =========================================================================

    private TriggeredAbility BuildOnBattlefield(out Creature enchanter)
    {
        enchanter = WitchEnchanterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(enchanter);
        enchanter.SetZone(ZoneType.Battlefield);
        return enchanter.Abilities.OfType<TriggeredAbility>().Single();
    }

    [Fact]
    public void Resolve_AgentSetOpponentArtifactTarget_DestroysIt()
    {
        var trinket = new Artifact("Bob's Trinket", "{2}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(trinket);
        trinket.SetZone(ZoneType.Battlefield);

        var etb = BuildOnBattlefield(out _);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { trinket } });
        foreach (var effect in etb.Effects) effect.Execute();

        trinket.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(trinket);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(trinket);
    }

    [Fact]
    public void Resolve_AgentSetOpponentEnchantmentTarget_DestroysIt()
    {
        var aura = new Enchantment("Bob's Aura", "{1}{W}");
        aura.SetOwner(_bob);
        aura.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var etb = BuildOnBattlefield(out _);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { aura } });
        foreach (var effect in etb.Effects) effect.Execute();

        aura.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(aura);
    }

    [Fact]
    public void Resolve_AgentSetOwnArtifact_NotAnOpponents_DestroyNoOp()
    {
        // "an opponent controls" — the controller's own artifact is not a
        // legal target; resolution-time gate makes the destroy a no-op
        // (CR 608.2b).
        var ownTrinket = new Artifact("Alice's Trinket", "{1}");
        ownTrinket.SetOwner(_alice);
        ownTrinket.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ownTrinket);
        ownTrinket.SetZone(ZoneType.Battlefield);

        var etb = BuildOnBattlefield(out _);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ownTrinket } });
        foreach (var effect in etb.Effects) effect.Execute();

        ownTrinket.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(ownTrinket);
    }

    [Fact]
    public void Resolve_AgentSetCreatureTarget_DestroyNoOp()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var etb = BuildOnBattlefield(out _);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        foreach (var effect in etb.Effects) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Resolve_TargetLeftBattlefield_DestroyNoOp()
    {
        var trinket = new Artifact("Trinket", "{1}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(trinket);
        trinket.SetZone(ZoneType.Battlefield);

        var etb = BuildOnBattlefield(out _);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { trinket } });

        // Trinket leaves the battlefield between trigger pick and resolution.
        _bob.Zones.Battlefield.RemoveCard(trinket);
        _bob.Zones.Hand.AddCard(trinket);
        trinket.SetZone(ZoneType.Hand);

        foreach (var effect in etb.Effects) effect.Execute();

        trinket.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(trinket);
    }

    [Fact]
    public void Resolve_NoTarget_NoCandidate_IsCleanNoOp()
    {
        var etb = BuildOnBattlefield(out _);

        var act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };
        act.Should().NotThrow();

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NoTarget_OpponentArtifactOnBattlefield_FallbackDestroysIt()
    {
        // No agent set ChosenTargets. The deterministic fallback should pick
        // the first legal artifact/enchantment AN OPPONENT controls
        // (single-arg dispatcher posture). Opponents are supplied via the
        // overload that takes an opponent list.
        var oppArtifact = new Artifact("Bob's Trinket", "{1}");
        oppArtifact.SetOwner(_bob);
        oppArtifact.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(oppArtifact);
        oppArtifact.SetZone(ZoneType.Battlefield);

        var enchanter = WitchEnchanterFactory.Create(_alice, opponents: new[] { _bob });
        _alice.Zones.Battlefield.AddCard(enchanter);
        enchanter.SetZone(ZoneType.Battlefield);

        var etb = enchanter.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        oppArtifact.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(oppArtifact);
    }

    // =========================================================================
    // Back face — Witch-Blessed Meadow
    // =========================================================================

    [Fact]
    public void WitchBlessedMeadow_Identity_Land_OnBackFace()
    {
        var land = WitchBlessedMeadowFactory.Create(_alice);

        land.Name.Should().Be("Witch-Blessed Meadow");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Witch Enchanter");
        land.MdfcState.BackFaceName.Should().Be("Witch-Blessed Meadow");
        land.MdfcState.IsBackFace.Should().BeTrue();
        land.MdfcState.ActiveFaceName.Should().Be("Witch-Blessed Meadow");
    }

    [Fact]
    public void WitchBlessedMeadow_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Witch-Blessed Meadow", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Witch-Blessed Meadow");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void WitchBlessedMeadow_HasTapForWhiteManaAbility()
    {
        var land = WitchBlessedMeadowFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("ETB is a replacement, not a triggered ability (CR 614.1c)");
    }

    [Fact]
    public void WitchBlessedMeadow_EntersUntapped_WhenAgentPaysThreeLife()
    {
        var bus = new ReplacementBus();
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(_alice, agent);

        var land = WitchBlessedMeadowFactory.Create(_alice, replacements: bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Witch-Blessed Meadow enters untapped when the controller pays 3 life");
        _alice.LifeTotal.Should().Be(17, "paying 3 life drops Alice from 20 → 17");
    }

    [Fact]
    public void WitchBlessedMeadow_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(_alice, agent);

        var land = WitchBlessedMeadowFactory.Create(_alice, replacements: bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Witch-Blessed Meadow enters tapped when the controller declines");
        _alice.LifeTotal.Should().Be(20, "declining keeps Alice's life unchanged");
    }

    [Fact]
    public void WitchBlessedMeadow_EntersTapped_WhenControllerCannotPayThreeLife()
    {
        // CR 119.4 — you can't pay life you don't have. Below 3 life the
        // agent is never prompted; land enters tapped.
        var bus = new ReplacementBus();
        var poor = new Player("Poor", 20);
        poor.LoseLife(18); // life = 2
        var agent = new ScriptedAgent(); // no QueueYesNo — would throw if prompted
        AgentRegistry.Set(poor, agent);

        var land = WitchBlessedMeadowFactory.Create(poor, replacements: bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: poor));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            $"enters tapped when controller can't pay 3 life (life={poor.LifeTotal})");
        poor.LifeTotal.Should().Be(2, "life unchanged — no payment took place");
    }

    [Fact]
    public void WitchBlessedMeadow_EntersTapped_WhenNoAgentRegistered()
    {
        var bus = new ReplacementBus();

        var land = WitchBlessedMeadowFactory.Create(_alice, replacements: bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "no agent registered → default decline → enters tapped");
        _alice.LifeTotal.Should().Be(20);
    }
}
