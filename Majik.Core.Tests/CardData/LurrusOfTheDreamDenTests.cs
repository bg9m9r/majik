using FluentAssertions;
using Majik.Core.Abilities;
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
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Lurrus of the Dream-Den (Ikoria, {W}{B}).
///
/// Covers:
///   - Card shape (name, types, supertypes, subtypes, P/T, mana cost).
///   - Lifelink keyword presence + static-ability description.
///   - NamedCardFactory dispatch.
///   - Runtime gate behavior:
///       * legal grave-cast under all preconditions (controller's turn,
///         permanent mv ≤ 2 in controller's graveyard).
///       * once-per-turn enforcement.
///       * per-turn reset on TurnStartedEvent.
///       * mv > 2 rejected.
///       * instant/sorcery rejected (permanent-only).
///       * opponent's turn rejected.
///
/// Companion deck-construction rule (CR 702.139) is intentionally
/// deferred — not exercised here.
/// </summary>
public class LurrusOfTheDreamDenTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public LurrusOfTheDreamDenTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Lurrus_IsLegendaryCatNightmare_3_2_AtCostWB()
    {
        var lurrus = LurrusOfTheDreamDenFactory.Create(_alice);

        lurrus.Name.Should().Be("Lurrus of the Dream-Den");
        lurrus.ManaCost.Should().Be("{W}{B}");
        lurrus.HasType(CardType.Creature).Should().BeTrue();
        lurrus.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        lurrus.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        lurrus.HasSubtype(CardSubtype.Nightmare).Should().BeTrue();
        lurrus.BasePower.Should().Be(3);
        lurrus.BaseToughness.Should().Be(2);
        lurrus.Owner.Should().Be(_alice);
    }

    [Fact]
    public void Lurrus_HasLifelink_AndGraveyardCastStaticAbility()
    {
        var lurrus = LurrusOfTheDreamDenFactory.Create(_alice);

        lurrus.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Lifelink");

        var statics = lurrus.Abilities.OfType<StaticAbility>().ToList();
        statics.Should().NotBeEmpty();
        statics.Should().Contain(s =>
            s.Description.Contains("permanent spell")
            && s.Description.Contains("mana value 2 or less")
            && s.Description.Contains("graveyard"));
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LurrusOfTheDreamDen()
    {
        var card = NamedCardFactory.Create("Lurrus of the Dream-Den", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Lurrus of the Dream-Den");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        card.HasSubtype(CardSubtype.Nightmare).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(3);
        ((Creature)card).BaseToughness.Should().Be(2);
        card.Owner.Should().Be(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Lifelink");
    }

    [Fact]
    public async Task Lurrus_AllowsCastingMvZeroArtifactFromGraveyard_OnControllersTurn()
    {
        // Lurrus on battlefield + Mishra's Bauble (mv 0 artifact) in
        // controller's graveyard. Gate is reset for Alice's turn.
        var (lurrus, gate) = BuildLurrusOnBattlefield(_alice);
        gate.ResetForTurn(_alice);

        var bauble = new Artifact("Mishra's Bauble", "{0}") { Owner = _alice };
        bauble.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bauble);

        gate.CanCast(bauble, _alice).Should().BeTrue(
            "Lurrus is on battlefield, Alice owns the bauble, "
            + "bauble is in graveyard and is a permanent with mv 0 ≤ 2, "
            + "and it is Alice's turn with no Lurrus-cast performed yet.");

        // Cast it through the alt-cost path.
        var altCost = LurrusOfTheDreamDenFactory.BuildAlternativeCost(bauble, gate);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, bauble,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: altCost);

        bauble.Zone.Should().Be(ZoneType.Stack);
        spell.Resolve();

        // After resolution the gate notes the cast was performed (the
        // alt-cost OnResolved hook fires inside SpellCastFlow, even
        // though the engine still routes the resolved card to its
        // default destination — battlefield for a permanent).
        gate.HasCastThisTurn(_alice).Should().BeTrue();
    }

    [Fact]
    public void Lurrus_EnforcesOncePerTurn_SecondPermSpellRejected()
    {
        var (lurrus, gate) = BuildLurrusOnBattlefield(_alice);
        gate.ResetForTurn(_alice);

        var bauble1 = new Artifact("Mishra's Bauble", "{0}") { Owner = _alice };
        bauble1.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bauble1);

        var bauble2 = new Artifact("Mox Opal", "{0}") { Owner = _alice };
        bauble2.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bauble2);

        // First cast is legal.
        gate.CanCast(bauble1, _alice).Should().BeTrue();
        gate.NotePerformed(bauble1, _alice);

        // Second cast same turn rejected.
        gate.CanCast(bauble2, _alice).Should().BeFalse(
            "Lurrus permits only one grave-cast per turn (CR 118.9 / oracle text).");

        // Building an alt-cost is fine (per-card shape predicates pass),
        // but the alt-cost legality check delegates to the gate — so
        // CanCastFor returns false too.
        var altCost = LurrusOfTheDreamDenFactory.BuildAlternativeCost(bauble2, gate);
        altCost.CanCastFor(bauble2, _alice).Should().BeFalse();
    }

    [Fact]
    public void Lurrus_TurnReset_RefreshesPerTurnBudget()
    {
        var (lurrus, gate) = BuildLurrusOnBattlefield(_alice);
        gate.ResetForTurn(_alice);

        var bauble = new Artifact("Mishra's Bauble", "{0}") { Owner = _alice };
        bauble.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bauble);

        gate.NotePerformed(bauble, _alice);
        gate.CanCast(bauble, _alice).Should().BeFalse("used the slot this turn");

        // Next turn — first Bob's, then Alice's.
        gate.ResetForTurn(_bob);
        gate.CanCast(bauble, _alice).Should().BeFalse(
            "still illegal — it's Bob's turn, not Alice's.");

        gate.ResetForTurn(_alice);
        gate.CanCast(bauble, _alice).Should().BeTrue(
            "Alice's next turn has restored the per-turn slot.");
    }

    [Fact]
    public void Lurrus_RejectsManaValueGreaterThanTwo()
    {
        var (lurrus, gate) = BuildLurrusOnBattlefield(_alice);
        gate.ResetForTurn(_alice);

        var grizzly = new Creature("Bears", "{1}{G}", 2, 2) { Owner = _alice };
        grizzly.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(grizzly);
        // sanity
        grizzly.ManaCostValue.TotalValue.Should().Be(2);

        // mv 2 is legal (2 ≤ 2).
        gate.CanCast(grizzly, _alice).Should().BeTrue();

        var bigger = new Creature("Hill Giant", "{3}{R}", 3, 3) { Owner = _alice };
        bigger.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bigger);
        bigger.ManaCostValue.TotalValue.Should().Be(4);

        gate.CanCast(bigger, _alice).Should().BeFalse(
            "mv 4 > 2 — Lurrus only grants the cast for mana value 2 or less.");

        // BuildAlternativeCost should refuse outright.
        var act = () => LurrusOfTheDreamDenFactory.BuildAlternativeCost(bigger, gate);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mana value*");
    }

    [Fact]
    public void Lurrus_RejectsInstantAndSorcery()
    {
        var (lurrus, gate) = BuildLurrusOnBattlefield(_alice);
        gate.ResetForTurn(_alice);

        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        gate.CanCast(bolt, _alice).Should().BeFalse(
            "Lurrus grants the cast only for PERMANENT spells. Instants don't qualify.");

        var ponder = new Sorcery("Ponder", "{U}") { Owner = _alice };
        ponder.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(ponder);

        gate.CanCast(ponder, _alice).Should().BeFalse(
            "Sorceries also don't qualify under the Lurrus oracle text.");

        // BuildAlternativeCost rejects non-permanent cards outright.
        var act = () => LurrusOfTheDreamDenFactory.BuildAlternativeCost(bolt, gate);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a permanent*");
    }

    [Fact]
    public void Lurrus_RejectsCastsOnOpponentsTurn()
    {
        var (lurrus, gate) = BuildLurrusOnBattlefield(_alice);

        var bauble = new Artifact("Mishra's Bauble", "{0}") { Owner = _alice };
        bauble.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bauble);

        // Bob's turn — "during each of YOUR turns" gates Alice out.
        gate.ResetForTurn(_bob);
        gate.CanCast(bauble, _alice).Should().BeFalse(
            "Lurrus only grants the cast during its controller's own turn.");

        // Switch to Alice's turn — now legal.
        gate.ResetForTurn(_alice);
        gate.CanCast(bauble, _alice).Should().BeTrue();
    }

    [Fact]
    public void Lurrus_BusOverload_AutoResetsOnTurnStarted()
    {
        // Bus-aware overload subscribes to TurnStartedEvent and calls
        // gate.ResetForTurn whenever a new turn begins.
        var lurrus = LurrusOfTheDreamDenFactory.Create(_alice, _bus);
        lurrus.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lurrus);
        var gate = LurrusOfTheDreamDenFactory.GetGate(lurrus);
        gate.Should().NotBeNull();

        var bauble = new Artifact("Mishra's Bauble", "{0}") { Owner = _alice };
        bauble.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bauble);

        // Initial state: no turn boundary observed → cast refused.
        gate!.CanCast(bauble, _alice).Should().BeFalse(
            "no TurnStartedEvent observed yet — gate cannot tell whose turn it is.");

        // Publish TurnStartedEvent(Alice) — gate observes the turn switch.
        _bus.Publish(new TurnStartedEvent(_alice, 1));
        gate.ActivePlayer.Should().Be(_alice);
        gate.CanCast(bauble, _alice).Should().BeTrue();

        // Consume the slot and advance to Bob's turn — slot resets but
        // Alice can no longer cast (not her turn).
        gate.NotePerformed(bauble, _alice);
        _bus.Publish(new TurnStartedEvent(_bob, 2));
        gate.ActivePlayer.Should().Be(_bob);
        gate.HasCastThisTurn(_alice).Should().BeFalse(
            "Bob's turn-start cleared the per-turn ledger.");
        gate.CanCast(bauble, _alice).Should().BeFalse("not Alice's turn anymore");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (Creature lurrus, LurrusGraveyardCastGate gate) BuildLurrusOnBattlefield(Player owner)
    {
        var lurrus = LurrusOfTheDreamDenFactory.Create(owner);
        lurrus.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(lurrus);
        var gate = LurrusOfTheDreamDenFactory.GetGate(lurrus)!;
        return (lurrus, gate);
    }
}
