using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// Rule 603.3b: when a single player has multiple triggers fire from the same
/// event, that player chooses the order. <see cref="TriggerManager"/> defers
/// to <see cref="IPlayerAgent.OrderTriggersAsync"/> when an agent map is
/// supplied; otherwise falls back to timestamp order (existing behavior).
/// </summary>
public class TriggerManagerAgentOrderingTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public TriggerManagerAgentOrderingTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
    }

    [Fact]
    public async Task DrainAsync_UsesAgentOrder_WithinSameController()
    {
        var (t1, t2, t3) = (MakeTrigger(_alice), MakeTrigger(_alice), MakeTrigger(_alice));
        Fire(t1); Fire(t2); Fire(t3);

        // Agent reverses
        var agent = new ScriptedAgent();
        agent.QueueTriggerOrder(new[] { (ITriggeredAbility)t3, t2, t1 });
        var agents = new Dictionary<Player, IPlayerAgent> { [_alice] = agent, [_bob] = new DeterministicBotAgent() };

        await _triggers.PutPendingTriggersOnStackAsync(_alice, agents, NewContext());

        // Last pushed ends up on top → first popped is t1 per agent order
        _stack.Pop().Should().BeSameAs(t1);
        _stack.Pop().Should().BeSameAs(t2);
        _stack.Pop().Should().BeSameAs(t3);
    }

    [Fact]
    public async Task DrainAsync_ApnapAcrossControllers_AgentOrdersWithin()
    {
        var aliceTrig = MakeTrigger(_alice);
        var bobTrig = MakeTrigger(_bob);
        Fire(aliceTrig); Fire(bobTrig);

        var agents = new Dictionary<Player, IPlayerAgent>
        {
            [_alice] = new DeterministicBotAgent(),
            [_bob] = new DeterministicBotAgent(),
        };

        await _triggers.PutPendingTriggersOnStackAsync(_alice, agents, NewContext());

        // Alice (active) pushed first → Bob ends up on top
        _stack.Pop().Should().BeSameAs(bobTrig);
        _stack.Pop().Should().BeSameAs(aliceTrig);
    }

    private TriggeredAbility MakeTrigger(Player controller)
    {
        var src = new Creature($"S-{Guid.NewGuid()}", "1G", 2, 2)
        {
            Owner = controller, Zone = ZoneType.Battlefield,
        };
        var ability = new TriggeredAbility(src, controller, Triggers.OnEnterBattlefieldSelf(src));
        _triggers.RegisterTriggeredAbility(ability);
        return ability;
    }

    private void Fire(TriggeredAbility ability)
    {
        var src = (ICard)ability.Source;
        _triggers.EvaluateTriggers(new CardMovedEvent(src, ZoneType.Hand, ZoneType.Battlefield));
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);
}
