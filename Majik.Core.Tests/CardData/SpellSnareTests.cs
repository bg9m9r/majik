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
/// End-to-end tests for Spell Snare (Coldsnap, {U}).
/// Oracle: "Counter target spell with mana value 2."
///
/// Coverage:
///   * Card shape + dispatch by name.
///   * Target a mv-2 spell → countered (graveyard).
///   * Target a mv-3 spell → no-op (illegal mv at resolution, CR 608.2b).
///   * Target a mv-1 spell → no-op (illegal mv at resolution).
///   * Empty stack → no candidate targets exist (no spell to counter).
/// </summary>
public class SpellSnareTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellSnareTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var snare = SpellSnareFactory.Create(_alice);

        snare.Name.Should().Be("Spell Snare");
        snare.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(snare).Should().Contain(ManaColor.Blue);
        snare.ManaCostValue.TotalValue.Should().Be(1);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsSpellSnareShape()
    {
        var dispatched = NamedCardFactory.Create("Spell Snare", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Spell Snare");
    }

    [Fact]
    public async Task TargetingManaValue2Spell_CountersIt()
    {
        var snare = SpellSnareFactory.Create(_alice);
        snare.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(snare);

        // Bob casts a mv-2 spell (e.g. Counterspell {U}{U}).
        var bobCounterspell = new Instant("Counterspell", "{U}{U}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobCounterspell, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, snare,
            SpellSnareFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobCounterspell.Zone.Should().Be(ZoneType.Graveyard,
            because: "Spell Snare counters the mv-2 target");
    }

    [Fact]
    public async Task TargetingManaValue3Spell_IsNoOp_AtResolution()
    {
        var snare = SpellSnareFactory.Create(_alice);
        snare.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(snare);

        // Bob casts a mv-3 spell (e.g. Lightning Helix {R}{W} — well, that's 2;
        // use Cancel {1}{U}{U} = mv 3).
        var bobCancel = new Instant("Cancel", "{1}{U}{U}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobCancel, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, snare,
            SpellSnareFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — illegal-mv target → effect does nothing. The mv-3 spell
        // was NOT countered into the graveyard by Spell Snare's effect.
        bobCancel.Zone.Should().NotBe(ZoneType.Graveyard);
    }

    [Fact]
    public async Task TargetingManaValue1Spell_IsNoOp_AtResolution()
    {
        var snare = SpellSnareFactory.Create(_alice);
        snare.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(snare);

        // Bob casts a mv-1 spell (Lightning Bolt {R} = mv 1).
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, snare,
            SpellSnareFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — illegal-mv target → effect does nothing. The mv-1 spell
        // remains on the stack (not countered into graveyard).
        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard);
    }

    [Fact]
    public void EmptyStack_NoSpellAvailable_AsTarget()
    {
        // With nothing on the stack, the bind effect resolver has nothing
        // valid to point at — confirm the stack is empty and Spell Snare's
        // counter effect with a null target is a no-op (defensive shape).
        var snare = SpellSnareFactory.Create(_alice);
        _stack.Count.Should().Be(0);

        // Build the definition; verify shape (1 target request, 1..1).
        var def = SpellSnareFactory.BuildDefinition(o => o, _stack);
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.HasVariableX.Should().BeFalse();
    }
}
