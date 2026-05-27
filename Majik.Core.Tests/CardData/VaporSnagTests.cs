using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Vapor Snag, Gut Shot, and Dismember (New Phyrexia).
///
/// Coverage:
/// - Vapor Snag: card shape, NamedCardFactory dispatch.
/// - Vapor Snag: creature returns to hand, controller loses 1 life.
/// - Vapor Snag: target not on battlefield at resolution → no-op, no life loss.
/// - Gut Shot: card shape, NamedCardFactory dispatch.
/// - Gut Shot: deals 1 damage to a creature target.
/// - Gut Shot: PhyrexianAlternativeCost parses {R/P} as 2-life alt cost.
/// - Dismember: card shape, NamedCardFactory dispatch.
/// - Dismember: target creature gets -5/-5 until end of turn.
/// - Dismember: -5/-5 expires at end of turn.
/// - Dismember: target not on battlefield at resolution → no-op.
/// </summary>
public class VaporSnagTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public VaporSnagTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Vapor Snag shape ─────────────────────────────────────────────────────

    [Fact]
    public void VaporSnag_Create_HasInstantShape_Blue()
    {
        var vs = VaporSnagFactory.Create(_alice);

        vs.Name.Should().Be("Vapor Snag");
        vs.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(vs).Should().Contain(ManaColor.Blue);
        vs.Owner.Should().Be(_alice);
        vs.Controller.Should().Be(_alice);
        vs.ManaCostValue.TotalValue.Should().Be(1, because: "single {U} pip");
    }

    [Fact]
    public void VaporSnag_NamedCardFactory_ReturnsCorrectShape()
    {
        var card = NamedCardFactory.Create("Vapor Snag", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Vapor Snag");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    // ── Vapor Snag resolve effect ─────────────────────────────────────────────

    [Fact]
    public async Task VaporSnag_Resolve_CreatureReturnedToOwnerHand_ControllerLoses1Life()
    {
        // Bob controls a Grizzly Bears that he also owns.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        // Alice casts Vapor Snag targeting Bob's bear.
        var vs = VaporSnagFactory.Create(_alice);
        vs.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(vs);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var bobLifeBefore = _bob.LifeTotal;

        await _flow.CastAsync(
            _alice, vs,
            VaporSnagFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Bear is in Bob's hand.
        bear.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);

        // Bob (the controller of the creature) loses 1 life.
        _bob.LifeTotal.Should().Be(bobLifeBefore - 1,
            because: "Vapor Snag's second clause: 'its controller loses 1 life'");

        // Alice's life is unchanged.
        _alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public async Task VaporSnag_TargetLeavesBeforeResolution_NoOpNoBounceNoLifeLoss()
    {
        // Bob had a creature on the battlefield, but it has already left
        // (zone changed to Graveyard) by the time Vapor Snag resolves.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        // Do NOT put the bear on the battlefield — it's already gone.
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var vs = VaporSnagFactory.Create(_alice);
        vs.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(vs);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var bobLifeBefore = _bob.LifeTotal;

        await _flow.CastAsync(
            _alice, vs,
            VaporSnagFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — target is not on battlefield → both effects do nothing.
        bear.Zone.Should().Be(ZoneType.Graveyard,
            because: "illegal target at resolution: bear is not on the battlefield");
        _bob.Zones.Hand.GetCards().Should().NotContain(bear);
        _bob.LifeTotal.Should().Be(bobLifeBefore,
            because: "no life loss when the target fizzles (CR 608.2b)");
    }

    // ── Gut Shot shape ───────────────────────────────────────────────────────

    [Fact]
    public void GutShot_Create_HasInstantShape_Red()
    {
        var gs = GutShotFactory.Create(_alice);

        gs.Name.Should().Be("Gut Shot");
        gs.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(gs).Should().Contain(ManaColor.Red);
        gs.Owner.Should().Be(_alice);
        gs.Controller.Should().Be(_alice);
        gs.ManaCostValue.TotalValue.Should().Be(1, because: "single {R} pip");
    }

    [Fact]
    public void GutShot_NamedCardFactory_ReturnsCorrectShape()
    {
        var card = NamedCardFactory.Create("Gut Shot", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Gut Shot");
    }

    [Fact]
    public void GutShot_PhyrexianAlternativeCost_TwoLifeZeroMana()
    {
        var alt = GutShotFactory.PhyrexianAlternativeCost();

        alt.LifeCost.Should().Be(2,
            because: "{R/P} has one phyrexian pip = 2 life");
        alt.AlternativeManaCost.Should().Be(ManaCost.Zero,
            because: "{R/P} has no non-phyrexian component after stripping the pip");
    }

    // ── Gut Shot resolve effect ───────────────────────────────────────────────

    [Fact]
    public async Task GutShot_Resolve_Deals1DamageToCreatureTarget()
    {
        // Bob controls a 2/2 bear.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var gs = GutShotFactory.Create(_alice);
        gs.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(gs);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var damageReceivedBefore = bear.Damage;

        await _flow.CastAsync(
            _alice, gs,
            GutShotFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bear.Damage.Should().Be(damageReceivedBefore + 1,
            because: "Gut Shot deals exactly 1 damage to the target creature");
    }

    // ── Dismember shape ───────────────────────────────────────────────────────

    [Fact]
    public void Dismember_Create_HasInstantShape_Black()
    {
        var d = DismemberFactory.Create(_alice);

        d.Name.Should().Be("Dismember");
        d.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(d).Should().Contain(ManaColor.Black);
        d.Owner.Should().Be(_alice);
        d.Controller.Should().Be(_alice);
        // {1}{B}{B} = 3 mana value
        d.ManaCostValue.TotalValue.Should().Be(3,
            because: "{1}{B}{B} has mana value 3");
    }

    [Fact]
    public void Dismember_NamedCardFactory_ReturnsCorrectShape()
    {
        var card = NamedCardFactory.Create("Dismember", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Dismember");
    }

    [Fact]
    public void Dismember_PhyrexianAlternativeCost_FourLifeOneMana()
    {
        var alt = DismemberFactory.PhyrexianAlternativeCost();

        alt.LifeCost.Should().Be(4,
            because: "{1}{B/P}{B/P} has two phyrexian pips = 4 life total");
        alt.AlternativeManaCost.TotalValue.Should().Be(1,
            because: "after stripping both phyrexian pips, the remainder is {1}");
    }

    // ── Dismember resolve effect ──────────────────────────────────────────────

    [Fact]
    public async Task Dismember_Resolve_TargetGetsMinusFiveMinusFive()
    {
        // Bob controls a 6/6 Tarmogoyf-like creature.
        var bigCreature = new Creature("Tarmogoyf", "{1}{G}", 6, 7)
        { Owner = _bob, Controller = _bob };
        bigCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bigCreature);

        var svc = new ContinuousEffectsService();
        bigCreature.ActiveEffects = svc;

        var d = DismemberFactory.Create(_alice);
        d.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(d);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bigCreature });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, d,
            DismemberFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // 6/7 + (-5/-5) = 1/2
        bigCreature.GetPower().Should().Be(1,
            because: "Dismember registers -5/-5 via PumpUntilEndOfTurnEffect");
        bigCreature.GetToughness().Should().Be(2);
    }

    [Fact]
    public async Task Dismember_MinusFiveMinusFive_ExpiresAtEndOfTurn()
    {
        // Bob controls a 6/7 creature.
        var bigCreature = new Creature("Tarmogoyf", "{1}{G}", 6, 7)
        { Owner = _bob, Controller = _bob };
        bigCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bigCreature);

        var svc = new ContinuousEffectsService();
        bigCreature.ActiveEffects = svc;

        var d = DismemberFactory.Create(_alice);
        d.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(d);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bigCreature });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, d,
            DismemberFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Confirm effect is active.
        bigCreature.GetPower().Should().Be(1, because: "-5/-5 is active during the turn");

        // Simulate end-of-turn cleanup (CR 514.2 — EOT effects expire).
        svc.ExpireEndOfTurn();

        // Effect has expired; back to base stats.
        bigCreature.GetPower().Should().Be(6,
            because: "PumpUntilEndOfTurnEffect.ExpiresAtEndOfTurn = true; effect removed at EOT");
        bigCreature.GetToughness().Should().Be(7);
    }

    [Fact]
    public async Task Dismember_TargetNotOnBattlefield_IsNoOp()
    {
        // Bob's creature is already in the graveyard.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var svc = new ContinuousEffectsService();
        bear.ActiveEffects = svc;

        var d = DismemberFactory.Create(_alice);
        d.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(d);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, d,
            DismemberFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — target not on battlefield → no-op.
        bear.GetPower().Should().Be(2,
            because: "Dismember does nothing when target is not on the battlefield");
        bear.GetToughness().Should().Be(2);
    }
}
