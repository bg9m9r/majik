using FluentAssertions;
using Majik.Core.CardData.Adventures;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Adventures;

/// <summary>
/// End-to-end coverage for the CR 715 Adventure cast pipeline:
///
///   1. Cast the Adventure half from hand via
///      <see cref="AdventureAlternativeCost"/>. The card resolves as the
///      printed Instant/Sorcery effect and lands in Exile (CR 715.3d) —
///      NOT in the graveyard, and NOT on the battlefield even though the
///      printed card is a Creature.
///   2. While exiled-as-Adventure, the owner may cast the creature face
///      from exile for its printed mana cost via the existing
///      <see cref="ExileCastAlternativeCost"/> probe surface (runtime
///      exile-cast grant stamped by AdventureAlternativeCost.OnResolved).
///   3. Once the creature face is cast from exile, the card leaves
///      exile — the next attempt at ExileCastAlternativeCost.CanCastFor
///      fails because the zone-gate (Zone == Exile) no longer holds.
///   4. Casting the creature face from hand still works normally via the
///      no-alt-cost path (regression guard — Adventure attachment must
///      not break the standard cast).
///   5. Bonecrusher Giant + Murderous Rider both flow end-to-end through
///      the same pipeline, proving the AdventureSpec attachment is the
///      sole per-card seam.
/// </summary>
public class AdventureCastPipelineTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public AdventureCastPipelineTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -----------------------------------------------------------------------
    // AdventureSpec attachment (sanity)
    // -----------------------------------------------------------------------

    [Fact]
    public void EmberethShieldbreaker_AttachesBattleDisplayAdventureSpec()
    {
        var card = EmberethShieldbreakerFactory.Create(_alice);

        card.AdventureSpec.Should().NotBeNull();
        card.AdventureSpec!.Name.Should().Be("Battle Display");
        card.AdventureSpec.IsSorcery.Should().BeTrue("Battle Display is a Sorcery");
        card.AdventureSpec.ManaCost.Generic.Should().Be(0);
        card.AdventureSpec.ManaCost.Red.Should().Be(1);
    }

    [Fact]
    public void BonecrusherGiant_AttachesStompAdventureSpec()
    {
        var card = BonecrusherGiantFactory.Create(_alice);

        card.AdventureSpec.Should().NotBeNull();
        card.AdventureSpec!.Name.Should().Be("Stomp");
        card.AdventureSpec.IsSorcery.Should().BeFalse("Stomp is an Instant");
        card.AdventureSpec.ManaCost.Generic.Should().Be(1);
        card.AdventureSpec.ManaCost.Red.Should().Be(1);
    }

    [Fact]
    public void MurderousRider_AttachesSwiftEndAdventureSpec()
    {
        var card = MurderousRiderFactory.Create(_alice);

        card.AdventureSpec.Should().NotBeNull();
        card.AdventureSpec!.Name.Should().Be("Swift End");
        card.AdventureSpec.IsSorcery.Should().BeTrue("Swift End is a Sorcery");
        card.AdventureSpec.ManaCost.Generic.Should().Be(1);
        card.AdventureSpec.ManaCost.Black.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Scenario 1 — cast Adventure half from hand → resolves to Exile
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BonecrusherGiant_CastStompFromHand_ResolvesToExile_NotGraveyard()
    {
        // ── Arrange ─────────────────────────────────────────────────────────
        var bcg = BonecrusherGiantFactory.Create(_alice);
        bcg.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bcg);

        var bobStartLife = _bob.LifeTotal;
        var advSpec = bcg.AdventureSpec!;
        var altCost = new AdventureAlternativeCost(advSpec.ManaCost, advSpec.IsSorcery);

        // ── Act ─────────────────────────────────────────────────────────────
        await CastAdventureAndResolveAsync(bcg, advSpec, altCost, target: _bob);

        // ── Assert ──────────────────────────────────────────────────────────
        bcg.Zone.Should().Be(ZoneType.Exile,
            because: "CR 715.3d — Adventure spells exile on resolve");
        _alice.Zones.Exile.GetCards().Should().Contain(bcg);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bcg);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bcg,
            because: "the creature face never landed on the battlefield");
        _bob.LifeTotal.Should().Be(bobStartLife - BonecrusherGiantFactory.StompDamage);
    }

    [Fact]
    public async Task MurderousRider_CastSwiftEndFromHand_ResolvesToExile_DestroysCreature_AliceLoses2Life()
    {
        // ── Arrange ─────────────────────────────────────────────────────────
        var mr = MurderousRiderFactory.Create(_alice);
        mr.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mr);

        // Bob controls a Bear that Swift End will kill.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _zones.MoveCard(bear, ZoneType.Library, ZoneType.Battlefield, _bob);

        var aliceStartLife = _alice.LifeTotal;
        var advSpec = mr.AdventureSpec!;
        var altCost = new AdventureAlternativeCost(advSpec.ManaCost, advSpec.IsSorcery);

        // ── Act ─────────────────────────────────────────────────────────────
        await CastAdventureAndResolveAsync(mr, advSpec, altCost, target: bear);

        // ── Assert ──────────────────────────────────────────────────────────
        mr.Zone.Should().Be(ZoneType.Exile, "CR 715.3d");
        _alice.Zones.Exile.GetCards().Should().Contain(mr);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(mr);

        bear.Zone.Should().Be(ZoneType.Graveyard, "Swift End destroys the creature");
        _alice.LifeTotal.Should().Be(aliceStartLife - MurderousRiderFactory.AdventureSelfLifeLoss,
            because: "Swift End's printed wording is 'you lose 2 life'");
    }

    // -----------------------------------------------------------------------
    // Scenario 2 — cast main face from exile after Adventure resolved
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BonecrusherGiant_AfterStompResolves_CanCastCreatureFaceFromExile_LandsOnBattlefield()
    {
        // ── Arrange — cast Stomp first so BCG sits in adventure-exile.
        var bcg = BonecrusherGiantFactory.Create(_alice);
        bcg.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bcg);

        var advSpec = bcg.AdventureSpec!;
        var stompAlt = new AdventureAlternativeCost(advSpec.ManaCost, advSpec.IsSorcery);
        await CastAdventureAndResolveAsync(bcg, advSpec, stompAlt, target: _bob);

        bcg.Zone.Should().Be(ZoneType.Exile);
        bcg.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            because: "AdventureAlternativeCost.OnResolved stamps the exile-cast grant");
        bcg.RuntimeExileCastCost.Should().NotBeNull();
        bcg.RuntimeExileCastCost!.Generic.Should().Be(2);
        bcg.RuntimeExileCastCost.Red.Should().Be(1);

        // ── Act — cast the creature face from exile for its printed cost.
        var exileCast = new ExileCastAlternativeCost("Adventure exile-cast", bcg.RuntimeExileCastCost);
        exileCast.CanCastFor(bcg, _alice).Should().BeTrue();

        var def = SpellDefinition.Vanilla(_ => Array.Empty<Majik.Core.Abilities.IEffect>());
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(_alice, bcg, def, agent, ctx, alternativeCost: exileCast);

        bcg.Zone.Should().Be(ZoneType.Stack);
        _resolver.ResolveTop(_stack);

        // ── Assert ──────────────────────────────────────────────────────────
        bcg.Zone.Should().Be(ZoneType.Battlefield,
            because: "the creature face resolves into the battlefield as normal");
        _alice.Zones.Battlefield.GetCards().Should().Contain(bcg);
        _alice.Zones.Exile.GetCards().Should().NotContain(bcg);
    }

    [Fact]
    public async Task MurderousRider_AfterSwiftEndResolves_CanCastCreatureFaceFromExile()
    {
        var mr = MurderousRiderFactory.Create(_alice);
        mr.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mr);

        // Give Swift End a target so it has something to destroy.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _zones.MoveCard(bear, ZoneType.Library, ZoneType.Battlefield, _bob);

        var advSpec = mr.AdventureSpec!;
        var swiftEndAlt = new AdventureAlternativeCost(advSpec.ManaCost, advSpec.IsSorcery);
        await CastAdventureAndResolveAsync(mr, advSpec, swiftEndAlt, target: bear);

        mr.Zone.Should().Be(ZoneType.Exile);
        mr.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        mr.RuntimeExileCastCost!.Generic.Should().Be(1);
        mr.RuntimeExileCastCost.Black.Should().Be(2);

        // Cast the creature face from exile.
        var exileCast = new ExileCastAlternativeCost("Adventure exile-cast", mr.RuntimeExileCastCost);
        var def = SpellDefinition.Vanilla(_ => Array.Empty<Majik.Core.Abilities.IEffect>());
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(_alice, mr, def, agent, ctx, alternativeCost: exileCast);
        _resolver.ResolveTop(_stack);

        mr.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(mr);
    }

    // -----------------------------------------------------------------------
    // Scenario 3 — once main face leaves exile, permission is revoked
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AfterMainFaceLeavesExile_ExileCastPermissionNoLongerApplies()
    {
        var bcg = BonecrusherGiantFactory.Create(_alice);
        bcg.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bcg);

        var advSpec = bcg.AdventureSpec!;
        var stompAlt = new AdventureAlternativeCost(advSpec.ManaCost, advSpec.IsSorcery);
        await CastAdventureAndResolveAsync(bcg, advSpec, stompAlt, target: _bob);

        // Cast creature face from exile → resolves to battlefield.
        var exileCast = new ExileCastAlternativeCost("Adventure exile-cast", bcg.RuntimeExileCastCost!);
        var def = SpellDefinition.Vanilla(_ => Array.Empty<Majik.Core.Abilities.IEffect>());
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(_alice, bcg, def, agent, ctx, alternativeCost: exileCast);
        _resolver.ResolveTop(_stack);

        bcg.Zone.Should().Be(ZoneType.Battlefield);

        // CR 715.3d — permission applies "as long as that card remains
        // exiled". The card is on the battlefield now, so the alt-cost
        // probe must refuse a re-cast attempt (Zone gate inside
        // ExileCastAlternativeCost.CanCastFor returns false).
        var probe = new ExileCastAlternativeCost("re-probe", bcg.RuntimeExileCastCost!);
        probe.CanCastFor(bcg, _alice).Should().BeFalse(
            because: "the card is no longer in exile, so the cast-from-exile gate fails");
    }

    // -----------------------------------------------------------------------
    // Scenario 4 — regression: casting creature face from hand still works
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CastingCreatureFaceFromHand_WorksNormally_Unchanged()
    {
        var bcg = BonecrusherGiantFactory.Create(_alice);
        bcg.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bcg);

        // No alt-cost — printed cast path.
        var def = SpellDefinition.Vanilla(_ => Array.Empty<Majik.Core.Abilities.IEffect>());
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(_alice, bcg, def, agent, ctx, alternativeCost: null);

        bcg.Zone.Should().Be(ZoneType.Stack);
        _resolver.ResolveTop(_stack);

        bcg.Zone.Should().Be(ZoneType.Battlefield,
            because: "the printed Creature face resolves to the battlefield");
        _alice.Zones.Battlefield.GetCards().Should().Contain(bcg);
    }

    // -----------------------------------------------------------------------
    // Scenario 5 — AdventureAlternativeCost.CanCastFor guards
    // -----------------------------------------------------------------------

    [Fact]
    public void AdventureAlternativeCost_CannotCastFor_NonAdventurerCard()
    {
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Hand);

        var alt = new AdventureAlternativeCost(ManaCost.Parse("R"), isSorcerySpeed: false);

        alt.CanCastFor(bolt, _alice).Should().BeFalse(
            because: "Lightning Bolt carries no AdventureSpec");
    }

    [Fact]
    public void AdventureAlternativeCost_CannotCastFor_CardOutsideHand()
    {
        var bcg = BonecrusherGiantFactory.Create(_alice);
        bcg.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bcg);

        var advSpec = bcg.AdventureSpec!;
        var alt = new AdventureAlternativeCost(advSpec.ManaCost, advSpec.IsSorcery);

        alt.CanCastFor(bcg, _alice).Should().BeFalse(
            because: "CR 715.3 — Adventure is cast from hand (this MVP surface; other zones via specific effects later)");
    }

    [Fact]
    public void AdventureAlternativeCost_PostResolutionZone_IsExile()
    {
        var alt = new AdventureAlternativeCost(ManaCost.Parse("R"), isSorcerySpeed: false);
        alt.PostResolutionZone.Should().Be(ZoneType.Exile, "CR 715.3d");
    }

    [Fact]
    public async Task AdventureSorceryCast_OutsideMainPhase_Throws()
    {
        var mr = MurderousRiderFactory.Create(_alice);
        mr.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mr);

        var advSpec = mr.AdventureSpec!;
        var alt = new AdventureAlternativeCost(advSpec.ManaCost, advSpec.IsSorcery);

        var def = advSpec.BuildDefinition(_alice, raw => raw);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { new Creature("Dummy", "G", 1, 1) });
        agent.QueueMana(ManaPayment.Empty);

        // Wrong phase — Upkeep rather than Main.
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Upkeep, _stack);

        Func<Task> act = async () => await _flow.CastAsync(
            _alice, mr, def, agent, ctx, alternativeCost: alt);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sorcery-speed restriction*");
    }

    // -----------------------------------------------------------------------
    // Helper — full SpellCastFlow → StackResolver round-trip via Adventure.
    // -----------------------------------------------------------------------

    private async Task CastAdventureAndResolveAsync(
        Card adventurerCard,
        AdventureSpec advSpec,
        AdventureAlternativeCost altCost,
        object target)
    {
        var def = advSpec.BuildDefinition(_alice, raw => raw);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(_alice, adventurerCard, def, agent, ctx, alternativeCost: altCost);

        _resolver.ResolveTop(_stack);
    }
}
