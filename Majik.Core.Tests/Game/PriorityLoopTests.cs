using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
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

    /// <summary>
    /// Regression test for CR 800.4a: a player who has already lost must not
    /// receive priority. Before the fix, <see cref="PriorityLoop"/> would
    /// prompt a lost player and execute any action they returned (including a
    /// CastSpell that called <c>Player.AddManaToPool</c> and threw
    /// "Cannot add mana after losing the game").
    ///
    /// With the fix: the loop detects <see cref="Player.HasLost"/> == true and
    /// immediately passes on that player's behalf, so the round ends cleanly
    /// and the agent for the lost player is never invoked.
    /// </summary>
    [Fact]
    public async Task LostPlayer_SkippedDuringPriorityRound_NoExceptionThrown()
    {
        // Bob has already lost (life = 0, MarkLost called — simulates SBA
        // running after combat damage before the post-combat priority round).
        _bob.MarkLost();

        // If the loop were to invoke Bob's agent it would throw. We use a
        // ThrowingAgent to make the failure visible rather than silently passing.
        var aliceAgent = new DeterministicBotAgent();
        var bobAgent   = new ThrowingAgent("Bob's agent must not be called after Bob has lost");

        var loop = NewLoop(aliceAgent, bobAgent);

        // Should complete without throwing — Bob's window is auto-passed.
        var act = async () => await loop.RunUntilRoundEndsAsync(_alice);
        await act.Should().NotThrowAsync();
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
            phaseAccessor: () => StepStateType.PreCombatMain,
            landDropTracker: new LandDropTracker());
    }

    /// <summary>
    /// Test helper: an agent that throws on <see cref="ChoosePriorityActionAsync"/>
    /// but delegates all other methods to an inner <see cref="DeterministicBotAgent"/>.
    /// Used to verify that a lost player's agent is NEVER prompted for priority
    /// by the <see cref="PriorityLoop"/> after <see cref="Player.MarkLost"/> is set.
    /// </summary>
    private sealed class ThrowingAgent : IPlayerAgent
    {
        private readonly string _message;
        private readonly DeterministicBotAgent _inner = new();
        public ThrowingAgent(string message) { _message = message; }

        public Task<PriorityAction> ChoosePriorityActionAsync(
            GameContext ctx, CancellationToken ct = default)
            => throw new InvalidOperationException(_message);

        // All other methods delegate to the deterministic inner agent.
        public Task<MulliganDecision> ChooseMulliganAsync(
            GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => _inner.ChooseMulliganAsync(ctx, hand, mulligansTaken, ct);

        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(
            GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => _inner.ChooseCardsToBottomAsync(ctx, hand, countToBottom, ct);

        public Task<IReadOnlyList<object>> ChooseTargetsAsync(
            GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => _inner.ChooseTargetsAsync(ctx, request, ct);

        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
            => _inner.ChooseXAsync(ctx, source, ct);

        public Task<int> ChooseModeAsync(
            GameContext ctx, IReadOnlyList<string> modes,
            IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => _inner.ChooseModeAsync(ctx, modes, modeIntents, ct);

        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(
            GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => _inner.OrderTriggersAsync(ctx, mine, ct);

        public Task<ManaPayment> ChooseManaSourcesAsync(
            GameContext ctx, ManaCost cost, CancellationToken ct = default)
            => _inner.ChooseManaSourcesAsync(ctx, cost, ct);

        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Permanent> eligibleAttackers, CancellationToken ct = default)
            => _inner.DeclareAttackersAsync(ctx, eligibleAttackers, ct);

        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Permanent> attackers, IReadOnlyList<Permanent> eligibleBlockers, CancellationToken ct = default)
            => _inner.DeclareBlockersAsync(ctx, attackers, eligibleBlockers, ct);

        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(
            GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => _inner.ChooseScryDecisionAsync(ctx, peeked, ct);

        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(
            GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => _inner.ChooseSurveilDecisionAsync(ctx, peeked, ct);
    }
}
