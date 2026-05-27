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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Quench (Ravnica Allegiance, {1}{U}).
/// Oracle: "Counter target spell unless its controller pays {1}."
///
/// Coverage:
///   * Card shape + dispatch by name (Instant {1}{U}, blue).
///   * SpellDefinition shape (1 target spell request).
///   * Counter when controller cannot pay {1} → spell to graveyard (CR 701.5).
///   * No-op when controller auto-pays {1} (CR 118.4).
/// </summary>
public class QuenchFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public QuenchFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue_OneU()
    {
        var quench = QuenchFactory.Create(_alice);

        quench.Name.Should().Be("Quench");
        quench.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(quench).Should().Contain(ManaColor.Blue);
        quench.ManaCost.Should().Be("{1}{U}");
        quench.ManaCostValue.TotalValue.Should().Be(2);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsQuenchShape()
    {
        var dispatched = NamedCardFactory.Create("Quench", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Quench");
        dispatched.ManaCost.Should().Be("{1}{U}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetSpellRequest()
    {
        var def = QuenchFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("target spell");
    }

    [Fact]
    public async Task Counters_TargetSpell_WhenControllerCannotPayOne()
    {
        var quench = QuenchFactory.Create(_alice);
        quench.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(quench);

        // Bob has 0 mana → cannot pay {1} → Quench counters.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, quench,
            QuenchFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Bob couldn't pay {1} so Quench counters his spell");
    }

    [Fact]
    public async Task DoesNotCounter_WhenControllerPaysOne()
    {
        var quench = QuenchFactory.Create(_alice);
        quench.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(quench);

        // Bob has {1} available in his mana pool — he auto-pays the rider.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(1));

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, quench,
            QuenchFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Bob paid {1} so Quench is countered into a no-op (CR 118.4)");
    }
}
