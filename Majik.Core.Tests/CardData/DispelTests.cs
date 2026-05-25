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
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Dispel (Rise of the Eldrazi, {U}).
/// Oracle: "Counter target instant spell."
///
/// Coverage:
///   * Card shape + dispatch by name.
///   * SpellDefinition shape (1 target instant spell request).
///   * Counters a targeted instant spell — lands in graveyard (CR 701.5).
///   * No-ops vs a sorcery / creature spell (CR 608.2b illegal-target).
/// </summary>
public class DispelTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public DispelTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var dispel = DispelFactory.Create(_alice);

        dispel.Name.Should().Be("Dispel");
        dispel.HasType(CardType.Instant).Should().BeTrue();
        dispel.ManaCost.Should().Be("{U}");
        dispel.ManaCostValue.TotalValue.Should().Be(1);
        CardColors.GetColors(dispel).Should().Contain(ManaColor.Blue);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsDispelShape()
    {
        var dispatched = NamedCardFactory.Create("Dispel", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Dispel");
        dispatched.ManaCost.Should().Be("{U}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetInstantSpellRequest()
    {
        var def = DispelFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("instant spell");
    }

    [Fact]
    public async Task CountersInstantSpell()
    {
        var dispel = DispelFactory.Create(_alice);
        dispel.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dispel);

        // Bob casts an instant (Lightning Bolt {R}).
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, dispel,
            DispelFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Dispel counters the targeted instant spell");
    }

    [Fact]
    public async Task DoesNotCounterSorcerySpell()
    {
        var dispel = DispelFactory.Create(_alice);
        dispel.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dispel);

        // Bob casts a sorcery (Wrath of God {2}{W}{W}).
        var bobWrath = new Sorcery("Wrath of God", "{2}{W}{W}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobWrath, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, dispel,
            DispelFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — sorcery target is illegal for Dispel at resolution.
        // The sorcery is NOT countered by Dispel.
        bobWrath.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Dispel does not counter sorcery spells");
    }

    [Fact]
    public async Task DoesNotCounterCreatureSpell()
    {
        var dispel = DispelFactory.Create(_alice);
        dispel.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dispel);

        // Bob casts a creature spell.
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, dispel,
            DispelFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Dispel does not counter creature spells");
    }
}
