using FluentAssertions;
using Majik.Core.Abilities;
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
/// End-to-end tests for Negate (various sets, {1}{U}).
/// Oracle: "Counter target noncreature spell."
///
/// Coverage:
///   * Card shape + dispatch by name.
///   * SpellDefinition shape (1 target noncreature spell request).
///   * Counter a noncreature spell → lands in graveyard (CR 701.5).
///   * Target is a creature spell → no-op at resolution (CR 608.2b).
/// </summary>
public class NegateTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public NegateTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var negate = NegateFactory.Create(_alice);

        negate.Name.Should().Be("Negate");
        negate.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(negate).Should().Contain(ManaColor.Blue);
        negate.ManaCostValue.TotalValue.Should().Be(2);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsNegateShape()
    {
        var dispatched = NamedCardFactory.Create("Negate", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Negate");
        dispatched.ManaCost.Should().Be("{1}{U}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetNoncreatureSpellRequest()
    {
        var def = NegateFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("noncreature");
    }

    // -----------------------------------------------------------------------
    // Counter a noncreature spell
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CountersNoncreatureSpell()
    {
        var negate = NegateFactory.Create(_alice);
        negate.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(negate);

        // Bob casts a noncreature spell (Lightning Bolt {R}).
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, negate,
            NegateFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Negate counters the noncreature spell");
    }

    // -----------------------------------------------------------------------
    // Creature spell — no-op
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DoesNotCounterCreatureSpell()
    {
        var negate = NegateFactory.Create(_alice);
        negate.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(negate);

        // Bob casts a creature spell (Grizzly Bears {1}{G}).
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, negate,
            NegateFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — creature spell is an illegal target at resolution.
        // Negate's effect does nothing for it; the creature spell is NOT
        // sent to the graveyard by Negate (it may resolve normally).
        bobBear.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Negate does not counter creature spells");
    }
}
