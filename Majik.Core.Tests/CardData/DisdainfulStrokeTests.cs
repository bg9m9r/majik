using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Disdainful Stroke (Khans of Tarkir, {1}{U}).
/// Oracle: "Counter target spell with mana value 4 or greater."
///
/// Coverage:
///   * Card shape + dispatch by name.
///   * Target a mv-4 spell → countered (graveyard).
///   * Target a mv-5 spell → countered (graveyard).
///   * Target a mv-3 spell → no-op (illegal mv at resolution, CR 608.2b).
///   * Empty stack → defensive no-op + shape check.
/// </summary>
public class DisdainfulStrokeTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public DisdainfulStrokeTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var stroke = DisdainfulStrokeFactory.Create(_alice);

        stroke.Name.Should().Be("Disdainful Stroke");
        stroke.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(stroke).Should().Contain(ManaColor.Blue);
        stroke.ManaCostValue.TotalValue.Should().Be(2);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsDisdainfulStrokeShape()
    {
        var dispatched = NamedCardFactory.Create("Disdainful Stroke", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Disdainful Stroke");
        dispatched.ManaCost.Should().Be("{1}{U}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetSpellRequest()
    {
        var def = DisdainfulStrokeFactory.BuildDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("mana value 4 or greater");
    }

    [Fact]
    public async Task TargetingManaValue4Spell_CountersIt()
    {
        var stroke = DisdainfulStrokeFactory.Create(_alice);
        stroke.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(stroke);

        // Bob casts a mv-4 spell ({2}{U}{U} = mv 4).
        var bobSpellCard = new Instant("Mystic Confluence", "{3}{U}{U}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobSpellCard, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, stroke,
            DisdainfulStrokeFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobSpellCard.Zone.Should().Be(ZoneType.Graveyard,
            because: "Disdainful Stroke counters a spell with mana value 4 or greater");
    }

    [Fact]
    public async Task TargetingManaValue4ExactSpell_CountersIt()
    {
        var stroke = DisdainfulStrokeFactory.Create(_alice);
        stroke.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(stroke);

        // Bob casts an exactly-mv-4 spell ({2}{U}{U} = mv 4).
        var bobSpellCard = new Instant("Cryptic Command", "{1}{U}{U}{U}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobSpellCard, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, stroke,
            DisdainfulStrokeFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // {1}{U}{U}{U} = mv 4 — the "or greater" boundary is inclusive.
        bobSpellCard.Zone.Should().Be(ZoneType.Graveyard,
            because: "mana value exactly 4 satisfies '4 or greater'");
    }

    [Fact]
    public async Task TargetingManaValue3Spell_IsNoOp_AtResolution()
    {
        var stroke = DisdainfulStrokeFactory.Create(_alice);
        stroke.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(stroke);

        // Bob casts a mv-3 spell (Cancel {1}{U}{U} = mv 3).
        var bobCancel = new Instant("Cancel", "{1}{U}{U}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobCancel, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, stroke,
            DisdainfulStrokeFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — illegal-mv target (mv 3 < 4) → effect does nothing.
        bobCancel.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Disdainful Stroke does not counter spells with mana value 3 or less");
    }

    [Fact]
    public void EmptyStack_NoSpellAvailable_AsTarget()
    {
        // With nothing on the stack, the bind effect resolver has nothing
        // valid to point at — confirm the stack is empty and the shape holds.
        var stroke = DisdainfulStrokeFactory.Create(_alice);
        _stack.Count.Should().Be(0);

        var def = DisdainfulStrokeFactory.BuildDefinition(o => o, _stack);
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.HasVariableX.Should().BeFalse();
    }
}
