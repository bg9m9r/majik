using FluentAssertions;
using Majik.Bot.Diagnostics;
using Majik.Server.Matches;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Pins the gating of the per-match bot-decision SignalR diagnostics channel
/// (<see cref="SignalrBotDecisionSink"/>) — the observer that feeds the
/// shipped, user-facing "bot decisions" panel.
///
/// <para>Regression: the live SignalR push used to be gated behind the dev
/// stdout logging flag (<c>Bot:DecisionLogging:Enabled</c>, default-off in
/// prod). That flag only governs the process-wide
/// <see cref="LoggerBotDecisionSink"/>; coupling the panel's wire channel to
/// it meant the panel was always empty in prod. The fix decouples them: the
/// SignalR sink is wired whenever a hub is present, independent of stdout
/// logging.</para>
///
/// <para>These tests exercise the exact production gating logic via
/// <see cref="MatchService.BuildPerMatchBotDecisionSink"/> (the same method the
/// vs-bot create path calls), so there is no game/engine async to wait on and
/// no parallelism added to the host-bound Server suite.</para>
/// </summary>
public sealed class MatchServiceBotDecisionSinkGatingTests
{
    private sealed record GroupCall(Guid MatchId, string Event, object Payload);

    private sealed class CaptureHub : IMatchHubPublisher
    {
        public List<GroupCall> Group { get; } = new();
        public void Publish(Guid matchId, string @event, object payload) =>
            Group.Add(new GroupCall(matchId, @event, payload));
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; } =
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private static BotDecision Decision(string chosen = "CastSpell:Lightning Bolt") => new(
        DecisionType: "Priority",
        Chosen: chosen,
        ChosenScore: 4.2,
        Alternatives: Array.Empty<BotDecisionAlternative>(),
        Context: new Dictionary<string, string> { ["turn"] = "3" });

    // NOTE: BuildPerMatchBotDecisionSink takes a hub + replay buffer but NOT
    // the BotDecisionLoggingEnabled flag — by construction the SignalR sink
    // can no longer depend on it. That structural fact is itself the fix; the
    // assertions below pin the resulting wire behaviour.

    [Fact]
    public void HubPresent_LoggingFlagOff_RecordsToBotDecisionChannel()
    {
        // The whole point of the bug fix: even with the dev stdout logging
        // flag OFF (its prod default), a recorded bot decision must reach the
        // "bot-decision" SignalR channel that the portal's panel listens on.
        var hub = new CaptureHub();
        var matchId = Guid.NewGuid();

        var sink = MatchService.BuildPerMatchBotDecisionSink(
            hub, replayBuffer: null, matchId);

        sink.Should().NotBeSameAs(NullBotDecisionSink.Instance,
            "a hub is present, so a SignalR diagnostics sink must be wired " +
            "regardless of Bot:DecisionLogging:Enabled");

        var decision = Decision();
        sink.Record(decision);

        hub.Group.Should().ContainSingle();
        hub.Group[0].MatchId.Should().Be(matchId);
        hub.Group[0].Event.Should().Be(SignalrBotDecisionSink.Channel);
        hub.Group[0].Event.Should().Be("bot-decision");
        hub.Group[0].Payload.Should().BeSameAs(decision);
    }

    [Fact]
    public void HubPresent_WithReplayBuffer_FansOutToBothObservers()
    {
        // The SignalR sink and the replay buffer are independent observers;
        // composing both must not drop the SignalR push (the panel) nor the
        // replay capture (the replay endpoint).
        var hub = new CaptureHub();
        var buffer = new MatchReplayBuffer(new FixedClock(), NullLogger<MatchReplayBuffer>.Instance);
        var matchId = Guid.NewGuid();

        var sink = MatchService.BuildPerMatchBotDecisionSink(hub, buffer, matchId);
        sink.Record(Decision("Pass"));

        hub.Group.Should().ContainSingle(
            "the user-facing SignalR diagnostics channel must still fire");
        hub.Group[0].Event.Should().Be("bot-decision");

        var replay = buffer.GetReplay(matchId);
        replay.Should().NotBeNull();
        replay!.EntryCount.Should().Be(1,
            "the always-on replay capture must still fire alongside SignalR");
    }

    [Fact]
    public void NoHub_NoReplay_ReturnsNullSink()
    {
        // No observers to wire → the no-op sentinel, so the create path skips
        // composing an extra sink entirely (zero overhead).
        var sink = MatchService.BuildPerMatchBotDecisionSink(
            hub: null, replayBuffer: null, Guid.NewGuid());

        sink.Should().BeSameAs(NullBotDecisionSink.Instance);
    }
}
