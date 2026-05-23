using FluentAssertions;
using Majik.Bot.Diagnostics;
using Majik.Server.Matches;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Tests for <see cref="SignalrBotDecisionSink"/>. Verifies that each
/// <see cref="BotDecision"/> ends up as a group-broadcast on the
/// <c>"bot-decision"</c> channel of the bound matchId, and that a faulty
/// hub publisher is swallowed (observer contract — engine must keep
/// running even if the wire is broken).
/// </summary>
public class SignalrBotDecisionSinkTests
{
    private sealed record GroupCall(Guid MatchId, string Event, object Payload);

    private sealed class CaptureHub : IMatchHubPublisher
    {
        public List<GroupCall> Group { get; } = new();
        public void Publish(Guid matchId, string @event, object payload) =>
            Group.Add(new GroupCall(matchId, @event, payload));
    }

    private sealed class ThrowingHub : IMatchHubPublisher
    {
        public int Calls { get; private set; }
        public void Publish(Guid matchId, string @event, object payload)
        {
            Calls++;
            throw new InvalidOperationException("simulated hub fault");
        }
    }

    private static BotDecision MakeDecision(string chosen = "CastSpell:Lightning Bolt") => new(
        DecisionType: "Priority",
        Chosen: chosen,
        ChosenScore: 4.2,
        Alternatives: new[]
        {
            new BotDecisionAlternative("Pass", 0.0),
            new BotDecisionAlternative("PlayLand:Mountain", 1.0),
        },
        Context: new Dictionary<string, string>
        {
            ["turn"] = "3",
            ["phase"] = "PreCombatMain",
            ["manaAvailable"] = "2",
        });

    [Fact]
    public void Record_ForwardsDecision_ToMatchGroup_OnBotDecisionChannel()
    {
        var hub = new CaptureHub();
        var matchId = Guid.NewGuid();
        var sink = new SignalrBotDecisionSink(matchId, hub);

        var decision = MakeDecision();
        sink.Record(decision);

        hub.Group.Should().ContainSingle();
        var call = hub.Group[0];
        call.MatchId.Should().Be(matchId);
        call.Event.Should().Be("bot-decision");
        // The BotDecision record is the wire payload — no DTO wrapping.
        // System.Text.Json handles the record + collection shapes by
        // default; we only check identity here, not serialization.
        call.Payload.Should().BeSameAs(decision);
    }

    [Fact]
    public void ChannelConstant_MatchesBridgeConvention()
    {
        // The channel name is part of the public wire contract — the
        // portal subscribes to this exact string. Lock it down so the
        // constant doesn't drift away from the bridge's "event"/"prompt"
        // naming convention.
        SignalrBotDecisionSink.Channel.Should().Be("bot-decision");
    }

    [Fact]
    public void Record_BindsToMatchId_AtConstruction()
    {
        // Each match gets its own sink instance; verify the captured
        // matchId is the one that shows up on the wire — not the most
        // recent record's, not whatever was last seen on the hub, etc.
        var hub = new CaptureHub();
        var matchA = Guid.NewGuid();
        var matchB = Guid.NewGuid();
        var sinkA = new SignalrBotDecisionSink(matchA, hub);
        var sinkB = new SignalrBotDecisionSink(matchB, hub);

        sinkA.Record(MakeDecision("A"));
        sinkB.Record(MakeDecision("B"));

        hub.Group.Should().HaveCount(2);
        hub.Group[0].MatchId.Should().Be(matchA);
        hub.Group[1].MatchId.Should().Be(matchB);
    }

    [Fact]
    public void Record_PublisherThrows_DoesNotPropagate()
    {
        // Observer contract: a broken hub must not abort the engine.
        // Logger is null so we exercise the catch-without-logger branch.
        var hub = new ThrowingHub();
        var sink = new SignalrBotDecisionSink(Guid.NewGuid(), hub);

        var act = () => sink.Record(MakeDecision());

        act.Should().NotThrow();
        hub.Calls.Should().Be(1);
    }

    [Fact]
    public void Constructor_NullHub_Throws()
    {
        var act = () => new SignalrBotDecisionSink(Guid.NewGuid(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_IsAllowed()
    {
        // Logger is optional — production wires one but tests / future
        // callers may omit it. The catch path checks for null before use.
        var hub = new CaptureHub();
        var act = () => new SignalrBotDecisionSink(Guid.NewGuid(), hub, logger: null);
        act.Should().NotThrow();
    }

    [Fact]
    public void Record_WithLogger_StillSucceeds_OnHappyPath()
    {
        // Just exercises the constructor overload that takes a logger —
        // the logger path is only reached on hub fault, which is covered
        // above. This test prevents the parameter from going stale.
        var hub = new CaptureHub();
        var sink = new SignalrBotDecisionSink(
            Guid.NewGuid(), hub, NullLogger<SignalrBotDecisionSink>.Instance);
        sink.Record(MakeDecision());
        hub.Group.Should().ContainSingle();
    }
}
