using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// Drives the priority-round loop (Rule 117) via async agent calls:
/// active player gets priority → on Pass, next player → when all pass in
/// succession, resolve top of stack (if any) and restart with active player,
/// or end the round if stack is empty.
/// </summary>
public class PriorityLoopTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;
    private readonly PriorityManager _priority;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public PriorityLoopTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
        _resolver = new StackResolver(_bus);
    }

    [Fact]
    public async Task EmptyStack_BothPlayersPass_RoundEnds_NoExceptions()
    {
        var loop = NewLoop(new DeterministicBotAgent(), new DeterministicBotAgent());

        await loop.RunUntilRoundEndsAsync(_alice);

        _stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task NonEmptyStack_BothPass_ResolvesTop_ThenLoopsAgain()
    {
        // Pre-load stack with a trigger that increments a counter on resolve.
        var ran = 0;
        var src = new Creature("S", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Battlefield };
        var ability = new TriggeredAbility(src, _alice,
            Triggers.OnEnterBattlefieldSelf(src),
            effects: new IEffect[] { new Effect("inc", () => ran++) });
        _stack.Push(ability);
        var loop = NewLoop(new DeterministicBotAgent(), new DeterministicBotAgent());

        await loop.RunUntilRoundEndsAsync(_alice);

        ran.Should().Be(1);
        _stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task ActivePlayerCastsLand_LoopPicksUpAction_ThenPasses()
    {
        var land = new Land("Mountain") { Owner = _alice, Zone = ZoneType.Hand };
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueuePriority(new PriorityAction.PlayLand(land));
        aliceAgent.QueuePriority(PriorityAction.Pass);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueuePriority(PriorityAction.Pass);
        bobAgent.QueuePriority(PriorityAction.Pass);

        var loop = NewLoop(aliceAgent, bobAgent);

        await loop.RunUntilRoundEndsAsync(_alice);

        land.Zone.Should().Be(ZoneType.Battlefield);
        _stack.IsEmpty.Should().BeTrue();
    }

    private PriorityLoop NewLoop(IPlayerAgent aliceAgent, IPlayerAgent bobAgent)
    {
        var agents = new Dictionary<Player, IPlayerAgent>
        {
            [_alice] = aliceAgent,
            [_bob] = bobAgent,
        };
        return new PriorityLoop(
            players: new[] { _alice, _bob },
            priority: _priority,
            stack: _stack,
            stackResolver: _resolver,
            zoneService: new ZoneService(_bus),
            agents: agents,
            turnNumberAccessor: () => 1,
            phaseAccessor: () => PhaseStateType.PreCombatMain,
            landDropTracker: new LandDropTracker());
    }
}
