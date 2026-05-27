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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Force of Negation (Modern Horizons, {1}{U}{U}).
/// Exercises:
///   * Card shape + dispatch.
///   * Pitch cast on opponent's turn — exiles a blue card, no life loss.
///   * Cast without pitch on own turn — works (uses printed mana cost).
///   * Counter target NONcreature spell — creature spell target is a no-op.
/// </summary>
public class ForceOfNegationFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ForceOfNegationFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var fon = ForceOfNegationFactory.Create(_alice);

        fon.Name.Should().Be("Force of Negation");
        fon.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(fon).Should().Contain(ManaColor.Blue);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsForceOfNegationFactoryShape()
    {
        var dispatched = NamedCardFactory.Create("Force of Negation", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Force of Negation");
    }

    [Fact]
    public async Task CastViaPitch_OnOpponentsTurn_ExilesBlueCard_NoLifeLoss()
    {
        var fon = ForceOfNegationFactory.Create(_alice);
        fon.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fon);

        var counterspell = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        counterspell.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(counterspell);

        var startingLife = _alice.LifeTotal;

        // Bob's noncreature spell on stack.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var pitchCost = new PitchAlternativeCost(ManaColor.Blue, counterspell, lifeCost: 0);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, fon,
            ForceOfNegationFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: pitchCost);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard);
        counterspell.Zone.Should().Be(ZoneType.Exile);
        _alice.LifeTotal.Should().Be(startingLife, because: "Force of Negation has no life rider");
    }

    [Fact]
    public async Task CastViaPitch_TargetingCreatureSpell_IsNoOp_AtResolution()
    {
        // Force of Negation's effect should refuse to counter a creature spell.
        // The cast itself succeeds (target selection isn't gated by template
        // text in this v1 stub); resolution sees the creature spell and bails
        // per CR 608.2b (illegal target → effect does nothing for that target).
        var fon = ForceOfNegationFactory.Create(_alice);
        fon.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fon);

        var pitchCard = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        pitchCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitchCard);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobCreatureSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobCreatureSpell);

        var pitchCost = new PitchAlternativeCost(ManaColor.Blue, pitchCard, lifeCost: 0);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobCreatureSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, fon,
            ForceOfNegationFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: pitchCost);

        _resolver.ResolveTop(_stack);

        // Creature spell was NOT countered (still on the stack OR moved by other
        // logic — but specifically not in graveyard by our counter effect).
        bobBear.Zone.Should().NotBe(ZoneType.Graveyard);
        // Pitch cost still resolved — Counterspell is exiled.
        pitchCard.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public async Task CastWithoutPitch_OnOwnTurn_Succeeds_PrintedCost()
    {
        // No alternative cost: SpellCastFlow charges the printed cost and casts
        // at instant speed (CR 117.1 — instants any time you have priority).
        var fon = ForceOfNegationFactory.Create(_alice);
        fon.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fon);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, fon,
            ForceOfNegationFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Force of Negation counters the noncreature target");
    }
}
