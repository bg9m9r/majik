using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.ValueObjects;
using Majik.Server.Matches;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// W6 integration coverage: the FAULT path end-to-end through a REAL engine
/// loop and the REAL <see cref="MatchFacadeBridge.SuperviseLoop"/> fault
/// continuation.
///
/// <para>This is the most faithful W6 test achievable WITHOUT the W7 DI wiring
/// (which connects the bridge's <c>onEngineErrored</c> callback to
/// <c>MatchService.OnEngineErrorAsync</c> via a fresh scope). Here we stand up
/// a genuine <see cref="GameFacade"/>, force its autonomous game loop to FAULT
/// (an agent throws during the very first mulligan decision — exactly the class
/// of "throw during autonomous progression" that the production root cause
/// swallows on the unobserved fire-and-forget task), capture the real
/// <see cref="GameFacade.FullGameTask"/>, and hand it to <c>SuperviseLoop</c>
/// wired to a capturing callback. We then assert the fault is surfaced as
/// <see cref="EngineFaultReason.Fault"/> carrying the thrown base exception.</para>
///
/// <para>What this proves: the launch-site change (capture the loop task) + the
/// bridge continuation together turn a previously-swallowed loop fault into a
/// prompt, observable engine-error report — instead of a silent human-priority
/// wedge.</para>
///
/// <para>Deferred to W7/W8: the LAST hop — that report reaching
/// <c>MatchService.OnEngineErrorAsync</c> via DI and transitioning the match to
/// <c>Errored</c> + publishing <c>match.engine-error</c> on the wire. That
/// requires the DI registration (W7); the full server-level e2e assertion lands
/// in W7/W8. The downstream CAS in <c>OnEngineErrorAsync</c> already makes a
/// double-report (this fault continuation AND a later watchdog tick) safe.</para>
/// </summary>
public sealed class SuperviseLoopFaultIntegrationTests
{
    /// <summary>An <see cref="IPlayerAgent"/> that throws on its first decision
    /// (the mulligan, which the driver awaits at the very top of the game loop),
    /// so the autonomous loop task FAULTS the way a card/trigger/bot throw would
    /// during real autonomous progression. Every other abstract member also
    /// throws — none is reached before the mulligan throw faults the loop.</summary>
    private sealed class ThrowingAgent : IPlayerAgent
    {
        private readonly Exception _boom;
        public ThrowingAgent(Exception boom) => _boom = boom;

        public Task<MulliganDecision> ChooseMulliganAsync(
            GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => throw _boom;

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => throw _boom;

        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(
            GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => throw _boom;

        public Task<IReadOnlyList<object>> ChooseTargetsAsync(
            GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => throw _boom;

        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
            => throw _boom;

        public Task<int> ChooseModeAsync(
            GameContext ctx, IReadOnlyList<string> modes,
            IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => throw _boom;

        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(
            GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => throw _boom;

        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
            => throw _boom;

        public Task<CombatPlan> DeclareAttackersAsync(
            GameContext ctx, IReadOnlyList<Permanent> eligibleAttackers, CancellationToken ct = default)
            => throw _boom;

        public Task<BlockPlan> DeclareBlockersAsync(
            GameContext ctx, IReadOnlyList<Permanent> attackers,
            IReadOnlyList<Permanent> eligibleBlockers, CancellationToken ct = default)
            => throw _boom;

        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(
            GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw _boom;

        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(
            GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw _boom;
    }

    private sealed record EngineErrorCall(Guid MatchId, EngineFaultReason Reason, Exception? Fault);

    [Fact]
    public async Task RealLoopFault_SuperviseLoop_ReportsFaultWithThrownException()
    {
        // ── A genuine facade with two minimal land-only decks. The decks only
        //    need to be legal enough to reach the mulligan decision, where the
        //    throwing agent faults the loop.
        var aliceDeck = new List<ICard>();
        for (var i = 0; i < 40; i++) aliceDeck.Add(new Land("Mountain"));
        var bobDeck = new List<ICard>();
        for (var i = 0; i < 40; i++) bobDeck.Add(new Land("Forest"));

        var facade = GameFacade.Create("Alice", "Bob", aliceDeck, bobDeck);

        var boom = new InvalidOperationException("card factory blew up mid-progression");
        // Both seats throw so whichever the driver asks first faults the loop.
        facade.ReplaceAliceAgent(new ThrowingAgent(boom));
        facade.ReplaceBobAgent(new ThrowingAgent(boom));

        // ── A REAL bridge wired to a capturing onEngineErrored (the seam W7 DI
        //    will connect to MatchService.OnEngineErrorAsync). No watchdog: this
        //    test isolates the FAULT continuation path, not the Hang path.
        var captured = new List<EngineErrorCall>();
        var firstError = new TaskCompletionSource<EngineErrorCall>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var bridge = new MatchFacadeBridge(
            new NoopHub(),
            NullLogger<MatchFacadeBridge>.Instance,
            replay: null,
            onActivePlayerChanged: null,
            watchdog: null,
            onEngineErrored: (matchId, reason, fault, _) =>
            {
                var call = new EngineErrorCall(matchId, reason, fault);
                lock (captured) captured.Add(call);
                firstError.TrySetResult(call);
                return Task.CompletedTask;
            });

        var matchId = Guid.NewGuid();

        // ── Mirror the production launch site (MatchService.StartGameForFirstPlayer
        //    after W6): start the loop fire-and-forget, capture its task, supervise.
        var loopTask = facade.StartFullGameAsync(firstPlayerSlot: 0, rng: new GameRandom(12345));
        bridge.SuperviseLoop(matchId, loopTask);

        // ── The loop must fault (the throwing mulligan agent) and the fault
        //    continuation must report it within a bounded wait.
        var call = await firstError.Task.WaitAsync(TimeSpan.FromSeconds(15));

        call.MatchId.Should().Be(matchId);
        call.Reason.Should().Be(EngineFaultReason.Fault,
            "a real loop fault is classified as Fault (vs the watchdog's Hang)");
        call.Fault.Should().BeSameAs(boom,
            "the thrown base exception is threaded through to onEngineErrored for server-side logging");
        captured.Should().HaveCount(1, "a single faulted loop reports exactly once via the continuation");
    }

    /// <summary>No-op hub — the bridge requires a publisher but this test never
    /// asserts on wire output (that lands in W7/W8 once OnEngineErrorAsync is
    /// wired via DI).</summary>
    private sealed class NoopHub : IMatchHubPublisher
    {
        public void Publish(Guid matchId, string @event, object payload) { }
        public void PublishPerRecipient(
            Guid matchId, string @event, IReadOnlyList<string> recipientSubs, Func<string, object> payloadFor) { }
        public void SendToConnection(string connectionId, string @event, object payload) { }
    }
}
