using FluentAssertions;
using Majik.Core.CardData;
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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Nourishing Shoal (Betrayers of Kamigawa, {X}{G}{G}, Instant — Arcane).
///
/// Oracle:
///   "You may exile a green card with mana value X from your hand rather
///    than pay this spell's mana cost.
///    You gain X life."
///
/// Covers:
///   - Card identity: Instant — Arcane, {X}{G}{G}, green, owner/controller.
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape: HasVariableX=true, no targets.
///   - Resolve at X=5 → caster gains 5 life.
///   - Resolve at X=0 → no life gained.
///   - BuildAlternativeCost helper / CanCastFor validation:
///       • green card in hand with MV = X → accepted.
///       • non-green card → rejected.
///       • green card with wrong MV → rejected.
///       • card not in hand (in graveyard) → rejected.
///       • card owned by opponent → rejected.
///       • the spell itself as pitch → rejected.
///   - End-to-end pitch cast: exile the pitched card on resolve, gain X life.
/// </summary>
public class NourishingShoalFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public NourishingShoalFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // ── Identity / dispatch ─────────────────────────────────────────────────

    [Fact]
    public void Create_IsInstantArcane_AtXGG_Green()
    {
        var shoal = NourishingShoalFactory.Create(_alice);

        shoal.Name.Should().Be("Nourishing Shoal");
        shoal.HasType(CardType.Instant).Should().BeTrue();
        shoal.HasSubtype(CardSubtype.Arcane).Should().BeTrue(
            "Nourishing Shoal is Instant — Arcane (CR 205.3k)");
        shoal.ManaCost.Should().Be("{X}{G}{G}");
        CardColors.GetColors(shoal).Should().Contain(ManaColor.Green);
        shoal.Owner.Should().BeSameAs(_alice);
        shoal.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchesNourishingShoal()
    {
        var card = NamedCardFactory.Create("Nourishing Shoal", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Nourishing Shoal");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{X}{G}{G}");
    }

    // ── SpellDefinition shape ───────────────────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_HasVariableX_NoTargets()
    {
        var def = NourishingShoalFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeTrue("X is declared at cast time");
        def.TargetRequests.Should().BeEmpty("Nourishing Shoal has no target clause");
        def.Modes.Should().BeEmpty();
    }

    // ── Resolution — life gain ──────────────────────────────────────────────

    [Fact]
    public void Resolve_GainsXLife_WhenXIsPositive()
    {
        var def = NourishingShoalFactory.BuildSpellDefinition(_alice);
        var aliceStart = _alice.LifeTotal;

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 5,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.LifeTotal.Should().Be(aliceStart + 5,
            "caster gains X (= 5) life on resolution (CR 119.4)");
    }

    [Fact]
    public void Resolve_XZero_NoLifeGained()
    {
        var def = NourishingShoalFactory.BuildSpellDefinition(_alice);
        var aliceStart = _alice.LifeTotal;

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 0,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.LifeTotal.Should().Be(aliceStart,
            "X = 0 produces no life gain");
    }

    // ── Alt-cost CanCastFor ─────────────────────────────────────────────────

    [Fact]
    public void AltCost_GreenCardInHand_CorrectMV_ReturnsTrue()
    {
        var shoal = ShoalInHand(_alice);
        // {1}{G} = MV 2. X = 2.
        var bear = GreenCardInHand(_alice, "{1}{G}");
        var cost = NourishingShoalFactory.BuildAlternativeCost(x: 2, bear);

        cost.CanCastFor(shoal, _alice).Should().BeTrue();
    }

    [Fact]
    public void AltCost_NonGreenCard_ReturnsFalse()
    {
        var shoal = ShoalInHand(_alice);
        // A blue card cannot pay the green pitch cost.
        var counterspell = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        counterspell.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(counterspell);

        var cost = NourishingShoalFactory.BuildAlternativeCost(x: 2, counterspell);

        cost.CanCastFor(shoal, _alice).Should().BeFalse(
            "exiled card must be green");
    }

    [Fact]
    public void AltCost_GreenCardWithWrongMV_ReturnsFalse()
    {
        var shoal = ShoalInHand(_alice);
        // Card MV is 2 ({1}{G}), but X was declared as 3.
        var bear = GreenCardInHand(_alice, "{1}{G}");
        var cost = NourishingShoalFactory.BuildAlternativeCost(x: 3, bear);

        cost.CanCastFor(shoal, _alice).Should().BeFalse(
            "exiled card MV must equal the declared X");
    }

    [Fact]
    public void AltCost_GreenCardNotInHand_ReturnsFalse()
    {
        var shoal = ShoalInHand(_alice);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        bear.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bear);

        var cost = NourishingShoalFactory.BuildAlternativeCost(x: 2, bear);

        cost.CanCastFor(shoal, _alice).Should().BeFalse(
            "exiled card must be in hand at announce time");
    }

    [Fact]
    public void AltCost_CardOwnedByOpponent_ReturnsFalse()
    {
        var shoal = ShoalInHand(_alice);
        var bear = GreenCardInHand(_bob, "{1}{G}");

        var cost = NourishingShoalFactory.BuildAlternativeCost(x: 2, bear);

        cost.CanCastFor(shoal, _alice).Should().BeFalse(
            "exiled card must be owned by the caster");
    }

    [Fact]
    public void AltCost_SpellItselfAsPitch_ReturnsFalse()
    {
        // The spell being cast cannot be the exiled card.
        var shoal = ShoalInHand(_alice);
        var cost = NourishingShoalFactory.BuildAlternativeCost(x: 2, shoal);

        cost.CanCastFor(shoal, _alice).Should().BeFalse(
            "the Shoal cannot exile itself as the pitch card");
    }

    [Fact]
    public void AltCost_AlternativeManaCost_IsZero()
    {
        var bear = GreenCardInHand(_alice, "{1}{G}");
        var cost = NourishingShoalFactory.BuildAlternativeCost(x: 2, bear);

        cost.AlternativeManaCost.Should().Be(ManaCost.Zero,
            "the exile is the entire cost — no mana is owed (CR 118.9)");
    }

    [Fact]
    public void AltCost_Description_MentionsColorAndMV()
    {
        var bear = GreenCardInHand(_alice, "{1}{G}");
        var cost = NourishingShoalFactory.BuildAlternativeCost(x: 2, bear);

        cost.Description.Should().Contain("Green");
        cost.Description.Should().Contain("2");
    }

    // ── OnResolved — exile ──────────────────────────────────────────────────

    [Fact]
    public void AltCost_OnResolved_ExilesCardFromHand()
    {
        var shoal = ShoalInHand(_alice);
        var bear = GreenCardInHand(_alice, "{1}{G}");
        var cost = NourishingShoalFactory.BuildAlternativeCost(x: 2, bear);

        cost.OnResolved(shoal, _alice);

        bear.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Hand.GetCards().Should().NotContain(bear);
        _alice.Zones.Exile.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void AltCost_OnResolved_CardAlreadyGone_DoesNotThrow()
    {
        var shoal = ShoalInHand(_alice);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        bear.SetZone(ZoneType.Exile); // already moved
        var cost = NourishingShoalFactory.BuildAlternativeCost(x: 2, bear);

        var act = () => cost.OnResolved(shoal, _alice);
        act.Should().NotThrow();
    }

    // ── End-to-end pitch cast ───────────────────────────────────────────────

    [Fact]
    public async Task PitchCast_ExilesGreenCardAndGainsXLife()
    {
        // Alice casts Nourishing Shoal via pitch: exile Primeval Titan ({4}{G}{G} = MV 6)
        // from hand as the alt cost, X = 6.
        var shoal = ShoalInHand(_alice);

        // Primeval Titan: {4}{G}{G} → MV 6.
        var titan = new Creature("Primeval Titan", "{4}{G}{G}", 6, 6) { Owner = _alice };
        titan.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(titan);

        var aliceStart = _alice.LifeTotal;
        const int x = 6;

        var pitchCost = NourishingShoalFactory.BuildAlternativeCost(x, titan);
        pitchCost.CanCastFor(shoal, _alice).Should().BeTrue(
            "green card with MV 6 qualifies when X = 6");

        var agent = new ScriptedAgent();
        agent.QueueX(x);
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, shoal,
            NourishingShoalFactory.BuildSpellDefinition(_alice),
            agent, ctx,
            alternativeCost: pitchCost);

        shoal.Zone.Should().Be(ZoneType.Stack);
        spell.Resolve();

        // Pitched card exiled.
        titan.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(titan);

        // Life gained.
        _alice.LifeTotal.Should().Be(aliceStart + x,
            $"caster gains X={x} life (CR 119.4)");
    }

    [Fact]
    public async Task RegularCast_WithMana_GainsXLife()
    {
        // Casting via mana (not pitch): pay {3}{G}{G} (X=3), gain 3 life.
        var shoal = ShoalInHand(_alice);
        var aliceStart = _alice.LifeTotal;
        const int x = 3;

        var agent = new ScriptedAgent();
        agent.QueueX(x);
        // Pay X + {G}{G} = 3 generic + 2 green = {3}{G}{G}.
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, shoal,
            NourishingShoalFactory.BuildSpellDefinition(_alice),
            agent, ctx);

        shoal.Zone.Should().Be(ZoneType.Stack);
        spell.Resolve();

        _alice.LifeTotal.Should().Be(aliceStart + x,
            $"caster gains X={x} life regardless of payment method (CR 119.4)");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private Instant ShoalInHand(Player owner)
    {
        var c = NourishingShoalFactory.Create(owner);
        c.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(c);
        return c;
    }

    private Creature GreenCardInHand(Player owner, string manaCost)
    {
        var c = new Creature("Green Creature", manaCost, 1, 1) { Owner = owner };
        c.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(c);
        return c;
    }
}
