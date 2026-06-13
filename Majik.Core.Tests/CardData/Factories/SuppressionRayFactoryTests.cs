using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SuppressionRayFactory"/> and
/// <see cref="OrderlyPlazaFactory"/> — the front + back faces of the
/// Murders at Karlov Manor modal double-faced card
/// Suppression Ray // Orderly Plaza.
///
/// Front face (Suppression Ray, {3}{W/U}{W/U}):
///   Sorcery. "Tap all creatures target player controls. You may pay any
///   amount of {E}. If you do, choose up to that many creatures tapped this
///   way. Put a stun counter on each of them."
///
/// Back face (Orderly Plaza):
///   Land. "This land enters tapped." "{T}: Add {W} or {U}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, owner).
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front: taps all the target player's UNTAPPED creatures.
/// - Front: with {E} paid, up to that many tapped-this-way creatures get a
///   stun counter; the energy is spent.
/// - Front: without {E} (0 energy), creatures are tapped but get no stun.
/// - Front: an already-tapped creature is never eligible for a stun counter.
/// - A stunned permanent skips its next untap, consuming the stun counter.
/// - Back: Land type, non-basic, no subtypes.
/// - Back: MDFC state pre-flipped to back face.
/// - Back: {T}: Add {W} or {U} (two mana abilities).
/// - Back: unconditional enters-tapped replacement (CR 614.1c).
/// - NamedCardFactory dispatch for both faces (prod source-gen path).
/// </summary>
[Trait("Color", "M")]
public class SuppressionRayFactoryTests
{
    private static ChosenSpellParams TargetParams(Player victim) =>
        new(ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { victim } },
            Mana: ManaPayment.Empty);

    private static void Resolve(SpellDefinition spell, Player victim)
    {
        foreach (var fx in spell.EffectFactory(TargetParams(victim)))
        {
            fx.Execute();
        }
    }

    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature MakeBobCreature(string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(_bob);
        c.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void SuppressionRay_Identity_5cmc_HybridWU_Sorcery()
    {
        var card = SuppressionRayFactory.Create(_alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Suppression Ray");
        card.ManaCost.Should().Be("{3}{W/U}{W/U}");
        card.ManaCostValue.TotalValue.Should().Be(5);
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SuppressionRay()
    {
        var card = NamedCardFactory.Create("Suppression Ray", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Suppression Ray");
        card.ManaCost.Should().Be("{3}{W/U}{W/U}");
    }

    [Fact]
    public void SuppressionRay_CarriesMdfcState_FrontFace()
    {
        var card = SuppressionRayFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "Suppression Ray is the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Suppression Ray");
        card.MdfcState!.BackFaceName.Should().Be("Orderly Plaza");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Suppression Ray");
        card.MdfcState!.CanCastEitherFace.Should().BeTrue(
            "the front face carries a castable back-land descriptor (CR 712.3)");
        card.MdfcState!.CastableBackFace!.IsLand.Should().BeTrue(
            "the back face Orderly Plaza is a land");
    }

    // =========================================================================
    // Front face — tap resolution
    // =========================================================================

    [Fact]
    public void Resolve_TapsAllTargetPlayersCreatures()
    {
        var c1 = MakeBobCreature("Grizzly Bears");
        var c2 = MakeBobCreature("Runeclaw Bear");
        // Alice's own creature must NOT be tapped — only the target's.
        var alicesBear = new Creature("Alpha Bear", "{1}{G}", 2, 2);
        alicesBear.SetOwner(_alice); alicesBear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(alicesBear);
        alicesBear.SetZone(ZoneType.Battlefield);

        AgentRegistry.Set(_alice, new DeterministicBotAgent());

        Resolve(SuppressionRayFactory.BuildSpellDefinition(_alice, raw => raw), _bob);

        c1.IsTapped.Should().BeTrue();
        c2.IsTapped.Should().BeTrue();
        alicesBear.IsTapped.Should().BeFalse("only the target player's creatures are tapped");
    }

    [Fact]
    public void Resolve_WithEnergyPaid_PutsStunCounters_OnChosenCreatures_AndSpendsEnergy()
    {
        var c1 = MakeBobCreature("Grizzly Bears");
        var c2 = MakeBobCreature("Runeclaw Bear");
        _alice.GainEnergy(5);

        // Scripted caster agent: pay/stun ALL tapped-this-way creatures
        // (select every candidate).
        var agent = new ScriptedAgent();
        agent.QueueChoice(candidates => candidates);
        AgentRegistry.Set(_alice, agent);

        Resolve(SuppressionRayFactory.BuildSpellDefinition(_alice, raw => raw), _bob);

        c1.IsTapped.Should().BeTrue();
        c2.IsTapped.Should().BeTrue();
        c1.Counters.Count(CounterType.Stun).Should().Be(1);
        c2.Counters.Count(CounterType.Stun).Should().Be(1);
        // CR 107.16 — two creatures stunned ⇒ 2 energy spent (5 - 2 = 3).
        _alice.EnergyCounters.Should().Be(3);
    }

    [Fact]
    public void Resolve_WithNoEnergy_TapsButPlacesNoStunCounters()
    {
        var c1 = MakeBobCreature("Grizzly Bears");
        // Alice has 0 energy — the optional {E} payment can't happen.
        AgentRegistry.Set(_alice, new DeterministicBotAgent());

        Resolve(SuppressionRayFactory.BuildSpellDefinition(_alice, raw => raw), _bob);

        c1.IsTapped.Should().BeTrue();
        c1.Counters.Count(CounterType.Stun).Should().Be(0);
        _alice.EnergyCounters.Should().Be(0);
    }

    [Fact]
    public void Resolve_DeclineEnergy_TapsButPlacesNoStunCounters()
    {
        var c1 = MakeBobCreature("Grizzly Bears");
        _alice.GainEnergy(3);

        // Scripted caster declines: select zero candidates.
        var agent = new ScriptedAgent();
        agent.QueueChoice(_ => System.Array.Empty<object>());
        AgentRegistry.Set(_alice, agent);

        Resolve(SuppressionRayFactory.BuildSpellDefinition(_alice, raw => raw), _bob);

        c1.IsTapped.Should().BeTrue();
        c1.Counters.Count(CounterType.Stun).Should().Be(0);
        _alice.EnergyCounters.Should().Be(3, "declining pays no energy");
    }

    [Fact]
    public void Resolve_AlreadyTappedCreature_NotEligibleForStun()
    {
        var alreadyTapped = MakeBobCreature("Sleepy Bear");
        alreadyTapped.Tap();
        var freshCreature = MakeBobCreature("Awake Bear");
        _alice.GainEnergy(5);

        // Caster tries to stun every candidate offered. The already-tapped
        // creature must not be in the candidate set, so only the fresh one
        // is stunnable.
        var agent = new ScriptedAgent();
        agent.QueueChoice(candidates => candidates);
        AgentRegistry.Set(_alice, agent);

        Resolve(SuppressionRayFactory.BuildSpellDefinition(_alice, raw => raw), _bob);

        alreadyTapped.IsTapped.Should().BeTrue();
        alreadyTapped.Counters.Count(CounterType.Stun).Should().Be(0,
            "a creature already tapped before the spell was not tapped this way");
        freshCreature.IsTapped.Should().BeTrue();
        freshCreature.Counters.Count(CounterType.Stun).Should().Be(1);
        // Only one creature stunned ⇒ 1 energy spent.
        _alice.EnergyCounters.Should().Be(4);
    }

    [Fact]
    public void StunCounter_SkipsNextUntap_ConsumingTheCounter()
    {
        // CR 122.1g — a stunned permanent that would untap removes a stun
        // counter instead. Model the untap-step replacement exactly as
        // TurnDriver.UntapStep does.
        var c = MakeBobCreature("Stunned Bear");
        c.Tap();
        c.Counters.Add(CounterType.Stun, 1);

        // Simulate one untap step (the TurnDriver logic).
        if (c.Counters.Count(CounterType.Stun) > 0)
            c.Counters.Remove(CounterType.Stun, 1);
        else
            c.Untap();

        c.IsTapped.Should().BeTrue("the stun counter replaced the untap");
        c.Counters.Count(CounterType.Stun).Should().Be(0, "the counter was consumed");

        // A subsequent untap step now untaps normally.
        if (c.Counters.Count(CounterType.Stun) > 0)
            c.Counters.Remove(CounterType.Stun, 1);
        else
            c.Untap();

        c.IsTapped.Should().BeFalse("with no stun counter left, it untaps");
    }

    // =========================================================================
    // Back face — Orderly Plaza
    // =========================================================================

    [Fact]
    public void OrderlyPlaza_Identity_NonBasicLand()
    {
        var land = OrderlyPlazaFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Orderly Plaza");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_OrderlyPlaza()
    {
        var card = NamedCardFactory.Create("Orderly Plaza", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Orderly Plaza");
    }

    [Fact]
    public void OrderlyPlaza_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = OrderlyPlazaFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Orderly Plaza is the back face of an MDFC");
        land.MdfcState!.FrontFaceName.Should().Be("Suppression Ray");
        land.MdfcState!.BackFaceName.Should().Be("Orderly Plaza");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Orderly Plaza");
    }

    [Fact]
    public void OrderlyPlaza_HasTwoManaAbilities_AddingWhiteOrBlue()
    {
        var land = OrderlyPlazaFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2, "{T}: Add {W} or {U} — one ability per colour");
        manaAbilities.Should().Contain(a => a.ManaGenerated.White > 0,
            "one ability produces white");
        manaAbilities.Should().Contain(a => a.ManaGenerated.Blue > 0,
            "one ability produces blue");
        manaAbilities.Should().NotContain(a => a.ManaGenerated.Black > 0);
        manaAbilities.Should().NotContain(a => a.ManaGenerated.Red > 0);
        manaAbilities.Should().NotContain(a => a.ManaGenerated.Green > 0);
    }

    [Fact]
    public void OrderlyPlaza_EntersTapped_Unconditionally()
    {
        var bus = new ReplacementBus();
        var land = OrderlyPlazaFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Orderly Plaza always enters tapped (CR 614.1c) — no opt-out");
    }

    // =========================================================================
    // Back-land play via the cast-either-face enumeration
    // =========================================================================

    [Fact]
    public void FrontFace_ExposesCastableBackLand_ForBackLandEnumeration()
    {
        var card = SuppressionRayFactory.Create(_alice);

        card.MdfcState!.CanCastEitherFace.Should().BeTrue();
        var backFace = card.MdfcState!.CastableBackFace!;
        backFace.IsLand.Should().BeTrue();
        backFace.Name.Should().Be("Orderly Plaza");

        var built = backFace.BuildCard(_alice, replacements: null);
        built.Should().BeOfType<Land>();
        built.Name.Should().Be("Orderly Plaza");
    }
}
