using System.Text.Json;
using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Server.Matches;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Tests for <see cref="MatchFacadeBridge"/>. Two layers:
///
/// 1. End-to-end attach/detach against a real <see cref="GameFacade"/>
///    — exercises facade.Subscribe / facade.SubscribePrompts wiring and
///    asserts the bridge's IDisposable subscriptions are released on
///    Detach (no leaked handlers).
///
/// 2. Direct unit tests against <see cref="MatchFacadeBridge.ForwardEvent"/>
///    / <c>ForwardPrompt</c> — driving the routing logic with synthesized
///    DTOs since fabricating engine-emitted DTOs through the public
///    facade surface requires standing up a full game loop.
/// </summary>
public class MatchFacadeBridgeTests
{
    // -----------------------------------------------------------------------
    // Fake hub publisher — captures Publish / PublishPerRecipient calls.
    // -----------------------------------------------------------------------

    private sealed record GroupCall(Guid MatchId, string Event, object Payload);
    private sealed record UserCall(Guid MatchId, string Event, string RecipientSub, object Payload);
    private sealed record ConnectionCall(string ConnectionId, string Event, object Payload);

    private sealed class CaptureHub : IMatchHubPublisher
    {
        public List<GroupCall> Group { get; } = new();
        public List<UserCall> PerRecipient { get; } = new();
        public List<ConnectionCall> Connection { get; } = new();

        public void Publish(Guid matchId, string @event, object payload) =>
            Group.Add(new GroupCall(matchId, @event, payload));

        public void PublishPerRecipient(
            Guid matchId,
            string @event,
            IReadOnlyList<string> recipientSubs,
            Func<string, object> payloadFor)
        {
            foreach (var sub in recipientSubs)
            {
                if (string.IsNullOrEmpty(sub)) continue;
                PerRecipient.Add(new UserCall(matchId, @event, sub, payloadFor(sub)));
            }
        }

        public void SendToConnection(string connectionId, string @event, object payload) =>
            Connection.Add(new ConnectionCall(connectionId, @event, payload));
    }

    private static MatchFacadeBridge BuildBridge(CaptureHub hub) =>
        new MatchFacadeBridge(hub, NullLogger<MatchFacadeBridge>.Instance);

    private static EventDto FakeEvent(string type = "TurnStartedEvent") => new(
        EventId: Guid.NewGuid(),
        Type: type,
        At: DateTime.UtcNow,
        Payload: JsonDocument.Parse("""{"hello":"world"}""").RootElement.Clone());

    private static EventDto FakeEvent(string type, string payloadJson) => new(
        EventId: Guid.NewGuid(),
        Type: type,
        At: DateTime.UtcNow,
        Payload: JsonDocument.Parse(payloadJson).RootElement.Clone());

    private static MatchFacadeBridge.PromptRouting DefaultRouting(
        Guid aliceId, Guid bobId,
        string creatorSub = "creator-sub", string opponentSub = "opponent-sub")
        => new(aliceId, bobId, creatorSub, opponentSub);

    // -----------------------------------------------------------------------
    // 1. End-to-end against a real GameFacade
    // -----------------------------------------------------------------------

    private static GameFacade BuildInertFacade()
    {
        // Empty decks: facade is constructed but no game is started, so
        // no engine events fire on their own — we're just exercising the
        // subscribe/unsubscribe contract.
        return GameFacade.Create("Alice", "Bob", new List<ICard>(), new List<ICard>());
    }

    // -----------------------------------------------------------------------
    // Clock-handoff wiring (Slice 1). The bridge fires the handoff callback
    // when the engine's active player changes seats. These focused tests
    // assert it does NOT fire spuriously while the active player is stable.
    // The positive turn-handoff path is covered end-to-end by
    // LayerAgreementInvariantTests.ClockHolder_FollowsEngineActivePlayer.
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // AssertAgreement — dev/test guard. Always present + unit-testable;
    // its CALL SITES are guarded to no-op in Release builds.
    // -----------------------------------------------------------------------

    [Fact]
    public void AssertAgreement_ClockHolderMismatch_Throws()
    {
        var active = Guid.NewGuid();
        var otherSeat = Guid.NewGuid();

        var act = () => MatchFacadeBridge.AssertAgreement(
            activePlayerId: active,
            clockHolderSeatId: otherSeat,
            wirePhase: "PreCombatMain");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*clock holder*engine active player*");
    }

    [Fact]
    public void AssertAgreement_RawMainPhase_Throws()
    {
        var seat = Guid.NewGuid();

        var act = () => MatchFacadeBridge.AssertAgreement(
            activePlayerId: seat,
            clockHolderSeatId: seat,
            wirePhase: "Main");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must be disambiguated*");
    }

    [Fact]
    public void AssertAgreement_InAgreement_DoesNotThrow()
    {
        var seat = Guid.NewGuid();

        var act = () => MatchFacadeBridge.AssertAgreement(
            activePlayerId: seat,
            clockHolderSeatId: seat,
            wirePhase: "PostCombatMain");

        act.Should().NotThrow();
    }

    [Fact]
    public void ForwardEvent_NoActivePlayerChange_DoesNotFireClockHandoff()
    {
        var hub = new CaptureHub();
        var fired = new List<string>();
        var bridge = new MatchFacadeBridge(
            hub, NullLogger<MatchFacadeBridge>.Instance, replay: null,
            onActivePlayerChanged: (_, sub, _, _) => { fired.Add(sub); return Task.CompletedTask; });

        var facade = BuildInertFacade(); // active player stays at Alice (seed)
        var matchId = Guid.NewGuid();
        bridge.Attach(matchId, "creator-sub", "opponent-sub", facade);

        var routing = DefaultRouting(facade.Alice.Id, facade.Bob.Id);
        // Forward several events — the inert facade never starts a turn, so
        // ActivePlayerId stays at the seed and no handoff should fire.
        for (var i = 0; i < 5; i++)
        {
            bridge.ForwardEvent(
                matchId,
                new EventEnvelope(FakeEvent("PriorityReceivedEvent"), PerPlayer: null),
                routing);
        }

        fired.Should().BeEmpty("the active player never changed — no clock handoff");
        bridge.ClockHandoffFireCount.Should().Be(0);
    }

    [Fact]
    public void ForwardEvent_NoCallbackWired_IsNoOp()
    {
        // Unit-test construction (no callback) must not throw on the
        // handoff path — it just skips it.
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub); // no onActivePlayerChanged
        var facade = BuildInertFacade();
        var matchId = Guid.NewGuid();
        bridge.Attach(matchId, "creator-sub", "opponent-sub", facade);

        var routing = DefaultRouting(facade.Alice.Id, facade.Bob.Id);
        var act = () => bridge.ForwardEvent(
            matchId, new EventEnvelope(FakeEvent("TurnStartedEvent"), PerPlayer: null), routing);

        act.Should().NotThrow();
        bridge.ClockHandoffFireCount.Should().Be(0);
    }

    [Fact]
    public void Attach_RegistersMatch_AndDetachReleasesIt()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var facade = BuildInertFacade();
        var matchId = Guid.NewGuid();

        bridge.IsAttached(matchId).Should().BeFalse();
        bridge.Attach(matchId, "creator-sub", "opponent-sub", facade);
        bridge.IsAttached(matchId).Should().BeTrue();
        bridge.ActiveCount.Should().Be(1);

        bridge.Detach(matchId);
        bridge.IsAttached(matchId).Should().BeFalse();
        bridge.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void Detach_IsIdempotent()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();

        // Detaching a never-attached match must not throw — terminal-
        // state handlers (concede / abandon / timeout) all funnel
        // through Detach, including on matches that never got a facade.
        bridge.Detach(matchId);
        bridge.Detach(matchId);
        bridge.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void Attach_Twice_ReplacesPriorAttachment()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var facade = BuildInertFacade();
        var matchId = Guid.NewGuid();

        bridge.Attach(matchId, "creator-sub", "opponent-sub", facade);
        bridge.Attach(matchId, "creator-sub", "opponent-sub", facade);

        // Only one active attachment for the match id (the second call
        // tore down the first before installing itself).
        bridge.ActiveCount.Should().Be(1);
        bridge.IsAttached(matchId).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // 2. Direct unit tests against ForwardEvent / ForwardPrompt
    // -----------------------------------------------------------------------

    [Fact]
    public void ForwardEvent_PublishesToMatchGroup_OnEventChannel()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var evt = FakeEvent("TurnStartedEvent");
        var envelope = new EventEnvelope(evt, PerPlayer: null);

        bridge.ForwardEvent(matchId, envelope, DefaultRouting(aliceId, bobId));

        hub.Group.Should().ContainSingle();
        hub.Group[0].MatchId.Should().Be(matchId);
        hub.Group[0].Event.Should().Be("event");
        hub.Group[0].Payload.Should().BeSameAs(evt);
        // Envelope.PerPlayer == null → group fan-out, no per-recipient
        // routing on this channel.
        hub.PerRecipient.Should().BeEmpty();
    }

    [Fact]
    public void ForwardEvent_PerPlayerEnvelope_RoutesPerRecipient_NoGroupFanout()
    {
        // CR 706: when the envelope carries per-player variants, each
        // seated sub receives ITS OWN payload via PublishPerRecipient —
        // never the other seat's variant. Group fan-out is bypassed
        // entirely so the unmasked variant can't leak.
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();

        var aliceView = FakeEvent("CardDrawnEvent", $$"""{"playerId":"{{aliceId}}","cardName":"Bolt"}""");
        var bobView = FakeEvent("CardDrawnEvent", $$"""{"playerId":"{{aliceId}}","hidden":true}""");
        var publicView = FakeEvent("CardDrawnEvent", $$"""{"playerId":"{{aliceId}}","cardName":"Bolt"}""");

        var envelope = new EventEnvelope(
            publicView,
            new Dictionary<Guid, EventDto> { [aliceId] = aliceView, [bobId] = bobView });

        bridge.ForwardEvent(matchId, envelope, DefaultRouting(aliceId, bobId));

        hub.Group.Should().BeEmpty();
        hub.PerRecipient.Should().HaveCount(2);
        // Creator (Alice's seat) sees the unmasked Alice view.
        var creatorCall = hub.PerRecipient.Single(c => c.RecipientSub == "creator-sub");
        creatorCall.Payload.Should().BeSameAs(aliceView);
        // Opponent (Bob's seat) sees the masked Bob view — never Alice's.
        var opponentCall = hub.PerRecipient.Single(c => c.RecipientSub == "opponent-sub");
        opponentCall.Payload.Should().BeSameAs(bobView);
        opponentCall.Payload.Should().NotBeSameAs(aliceView);
    }

    [Fact]
    public void ForwardEvent_PerPlayerEnvelope_SkipsBotRecipient()
    {
        // Bot seats have no SignalR connection — sending the per-recipient
        // event to "bot:*" is pointless. We must not skip the human seat
        // though, so a bot vs human match still gets the hidden-info
        // event delivered to the human player.
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var routing = DefaultRouting(aliceId, bobId, creatorSub: "creator-sub", opponentSub: "bot:archer");

        var aliceView = FakeEvent("CardDrawnEvent", $$"""{"playerId":"{{aliceId}}","cardName":"Bolt"}""");
        var bobView = FakeEvent("CardDrawnEvent", $$"""{"playerId":"{{aliceId}}","hidden":true}""");
        var publicView = aliceView;

        var envelope = new EventEnvelope(
            publicView,
            new Dictionary<Guid, EventDto> { [aliceId] = aliceView, [bobId] = bobView });

        bridge.ForwardEvent(matchId, envelope, routing);

        hub.Group.Should().BeEmpty();
        hub.PerRecipient.Should().ContainSingle();
        hub.PerRecipient[0].RecipientSub.Should().Be("creator-sub");
        hub.PerRecipient[0].Payload.Should().BeSameAs(aliceView);
    }

    [Fact]
    public void ForwardPrompt_AlicePrompt_GoesOnlyToCreator()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var routing = new MatchFacadeBridge.PromptRouting(
            aliceId, bobId, "creator-sub", "opponent-sub");

        var prompt = new PromptDto(Guid.NewGuid(), aliceId, new[] { "PassPriorityCommand" });
        bridge.ForwardPrompt(matchId, prompt, routing);

        hub.PerRecipient.Should().ContainSingle();
        var call = hub.PerRecipient[0];
        call.MatchId.Should().Be(matchId);
        call.Event.Should().Be("prompt");
        call.RecipientSub.Should().Be("creator-sub");
        call.Payload.Should().BeSameAs(prompt);
        // Opponent must not see the prompt.
        hub.PerRecipient.Should().NotContain(c => c.RecipientSub == "opponent-sub");
        hub.Group.Should().BeEmpty();
    }

    [Fact]
    public void ForwardPrompt_BobPrompt_GoesOnlyToOpponent()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var routing = new MatchFacadeBridge.PromptRouting(
            aliceId, bobId, "creator-sub", "opponent-sub");

        var prompt = new PromptDto(Guid.NewGuid(), bobId, new[] { "PassPriorityCommand" });
        bridge.ForwardPrompt(matchId, prompt, routing);

        hub.PerRecipient.Should().ContainSingle();
        hub.PerRecipient[0].RecipientSub.Should().Be("opponent-sub");
        hub.PerRecipient.Should().NotContain(c => c.RecipientSub == "creator-sub");
    }

    [Fact]
    public void ForwardPrompt_UnknownPlayerId_IsDropped()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var routing = new MatchFacadeBridge.PromptRouting(
            Guid.NewGuid(), Guid.NewGuid(), "creator-sub", "opponent-sub");

        var prompt = new PromptDto(Guid.NewGuid(), Guid.NewGuid(), new[] { "PassPriorityCommand" });
        bridge.ForwardPrompt(matchId, prompt, routing);

        // No hub send at all — better silence than spraying the prompt
        // to either seat with no way to verify provenance.
        hub.PerRecipient.Should().BeEmpty();
        hub.Group.Should().BeEmpty();
    }

    [Fact]
    public void ForwardPrompt_BotRecipient_IsSkipped()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var routing = new MatchFacadeBridge.PromptRouting(
            aliceId, bobId, "creator-sub", "bot:aggro");

        var prompt = new PromptDto(Guid.NewGuid(), bobId, new[] { "PassPriorityCommand" });
        bridge.ForwardPrompt(matchId, prompt, routing);

        // Bot subs have no SignalR connection — the bridge should drop
        // the send rather than create a phantom user-channel call.
        hub.PerRecipient.Should().BeEmpty();
    }

    [Fact]
    public void PromptRouting_ResolvesByPlayerId()
    {
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var routing = new MatchFacadeBridge.PromptRouting(
            aliceId, bobId, "creator-sub", "opponent-sub");

        routing.ResolveRecipientSub(aliceId).Should().Be("creator-sub");
        routing.ResolveRecipientSub(bobId).Should().Be("opponent-sub");
        routing.ResolveRecipientSub(Guid.NewGuid()).Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // 3. Per-recipient prompt buffer + replay-on-join
    //
    // These exercise the late-join race fix: the engine may publish a
    // prompt to the match group BEFORE the targeted player's SignalR
    // connection has joined (most acute on vs-Bot matches, see
    // MatchService.CreateBotMatchAsync). The bridge buffers the most-
    // recent prompt per (matchId, recipientSub) and replays it on
    // JoinMatch.
    // -----------------------------------------------------------------------

    [Fact]
    public void ForwardPrompt_BuffersPromptForLaterReplay()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var routing = new MatchFacadeBridge.PromptRouting(
            aliceId, bobId, "creator-sub", "opponent-sub");
        var prompt = new PromptDto(Guid.NewGuid(), aliceId, new[] { "PassPriorityCommand" });

        bridge.ForwardPrompt(matchId, prompt, routing);

        bridge.BufferedPromptCount.Should().Be(1);
        bridge.PeekBufferedPrompt(matchId, "creator-sub").Should().BeSameAs(prompt);
        bridge.PeekBufferedPrompt(matchId, "opponent-sub").Should().BeNull();
    }

    [Fact]
    public void ForwardPrompt_BotRecipient_IsNotBuffered()
    {
        // Bot seats run in-process — they don't have a SignalR
        // connection that could late-join. Buffering for them would
        // just leak memory until Detach.
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var routing = new MatchFacadeBridge.PromptRouting(
            aliceId, bobId, "creator-sub", "bot:aggro");
        var prompt = new PromptDto(Guid.NewGuid(), bobId, new[] { "PassPriorityCommand" });

        bridge.ForwardPrompt(matchId, prompt, routing);

        bridge.BufferedPromptCount.Should().Be(0);
    }

    [Fact]
    public void ForwardPrompt_NewPromptForSameRecipient_ReplacesBuffered()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var routing = new MatchFacadeBridge.PromptRouting(
            aliceId, bobId, "creator-sub", "opponent-sub");
        var first = new PromptDto(Guid.NewGuid(), aliceId, new[] { "PassPriorityCommand" });
        var second = new PromptDto(Guid.NewGuid(), aliceId, new[] { "MulliganDecisionCommand" });

        bridge.ForwardPrompt(matchId, first, routing);
        bridge.ForwardPrompt(matchId, second, routing);

        bridge.BufferedPromptCount.Should().Be(1);
        bridge.PeekBufferedPrompt(matchId, "creator-sub").Should().BeSameAs(second);
    }

    [Fact]
    public void ReplayPromptIfAny_SendsBufferedPromptToConnection()
    {
        // The core late-join replay path: engine published a prompt to
        // a (then-empty) group, client now joins → bridge re-sends the
        // buffered prompt to JUST that connection.
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var routing = new MatchFacadeBridge.PromptRouting(
            aliceId, bobId, "creator-sub", "opponent-sub");
        var prompt = new PromptDto(Guid.NewGuid(), aliceId, new[] { "MulliganDecisionCommand" });

        bridge.ForwardPrompt(matchId, prompt, routing);
        hub.Connection.Should().BeEmpty(); // not sent yet — only buffered + group-published

        bridge.ReplayPromptIfAny(matchId, "creator-sub", "conn-1");

        hub.Connection.Should().ContainSingle();
        var call = hub.Connection[0];
        call.ConnectionId.Should().Be("conn-1");
        call.Event.Should().Be("prompt");
        call.Payload.Should().BeSameAs(prompt);
        // Opponent connection joining must NOT receive the creator's
        // prompt — that would leak turn-timing info AND mis-render UI.
        hub.Connection.Should().NotContain(c => c.ConnectionId != "conn-1");
    }

    [Fact]
    public void ReplayPromptIfAny_NoBuffered_IsNoOp()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);

        bridge.ReplayPromptIfAny(Guid.NewGuid(), "creator-sub", "conn-1");

        hub.Connection.Should().BeEmpty();
    }

    [Fact]
    public void ReplayPromptIfAny_NotForOtherRecipient()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var routing = new MatchFacadeBridge.PromptRouting(
            aliceId, bobId, "creator-sub", "opponent-sub");
        var prompt = new PromptDto(Guid.NewGuid(), aliceId, new[] { "MulliganDecisionCommand" });

        bridge.ForwardPrompt(matchId, prompt, routing);

        // The opponent joining a match where ONLY the creator has a
        // buffered prompt must not receive that prompt.
        bridge.ReplayPromptIfAny(matchId, "opponent-sub", "conn-opp");
        hub.Connection.Should().BeEmpty();
    }

    [Fact]
    public void AckPrompt_ClearsBuffer_NoReplayAfter()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var routing = new MatchFacadeBridge.PromptRouting(
            aliceId, bobId, "creator-sub", "opponent-sub");
        var prompt = new PromptDto(Guid.NewGuid(), aliceId, new[] { "PassPriorityCommand" });

        bridge.ForwardPrompt(matchId, prompt, routing);
        bridge.AckPrompt(matchId, "creator-sub");

        bridge.PeekBufferedPrompt(matchId, "creator-sub").Should().BeNull();

        // A late-joining connection from the same recipient must NOT
        // receive the acked prompt.
        bridge.ReplayPromptIfAny(matchId, "creator-sub", "conn-1");
        hub.Connection.Should().BeEmpty();
    }

    [Fact]
    public void AckPrompt_IsIdempotent_AndIgnoresBot()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();

        // No-throw on unknown recipient (concurrent Detach race).
        bridge.AckPrompt(matchId, "creator-sub");
        bridge.AckPrompt(matchId, "creator-sub");

        // Bot recipient is a no-op — we never buffer for them.
        bridge.AckPrompt(matchId, "bot:aggro");

        bridge.BufferedPromptCount.Should().Be(0);
    }

    [Fact]
    public void Detach_ClearsAllBufferedPromptsForMatch()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchA = Guid.NewGuid();
        var matchB = Guid.NewGuid();
        var aliceA = Guid.NewGuid();
        var bobA = Guid.NewGuid();
        var aliceB = Guid.NewGuid();
        var bobB = Guid.NewGuid();
        var routingA = new MatchFacadeBridge.PromptRouting(
            aliceA, bobA, "creator-A", "opponent-A");
        var routingB = new MatchFacadeBridge.PromptRouting(
            aliceB, bobB, "creator-B", "opponent-B");

        bridge.ForwardPrompt(matchA, new PromptDto(Guid.NewGuid(), aliceA, new[] { "PassPriorityCommand" }), routingA);
        bridge.ForwardPrompt(matchA, new PromptDto(Guid.NewGuid(), bobA, new[] { "PassPriorityCommand" }), routingA);
        bridge.ForwardPrompt(matchB, new PromptDto(Guid.NewGuid(), aliceB, new[] { "PassPriorityCommand" }), routingB);

        bridge.BufferedPromptCount.Should().Be(3);

        bridge.Detach(matchA);

        // Only matchA's entries are gone — matchB is untouched.
        bridge.BufferedPromptCount.Should().Be(1);
        bridge.PeekBufferedPrompt(matchA, "creator-A").Should().BeNull();
        bridge.PeekBufferedPrompt(matchA, "opponent-A").Should().BeNull();
        bridge.PeekBufferedPrompt(matchB, "creator-B").Should().NotBeNull();
    }

    [Fact]
    public void EndToEnd_RaceFix_PromptPublishedBeforeJoin_ReplaysOnJoin()
    {
        // Simulate the CreateBotMatchAsync race directly:
        //   1. Engine emits prompt → ForwardPrompt fans out to the
        //      (empty) match group AND buffers per-recipient.
        //   2. Client navigates to /match/:id, hub.JoinMatch fires,
        //      bridge.ReplayPromptIfAny pushes the buffered prompt to
        //      the new connection on the "prompt" channel.
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var routing = new MatchFacadeBridge.PromptRouting(
            aliceId, bobId, "creator-sub", "bot:aggro");

        // (1) The bot match has just started. Engine publishes the
        //     creator's opening-hand mulligan prompt; group fan-out hits
        //     zero connections.
        var openingMulligan = new PromptDto(Guid.NewGuid(), aliceId, new[] { "MulliganDecisionCommand" });
        bridge.ForwardPrompt(matchId, openingMulligan, routing);
        hub.PerRecipient.Should().ContainSingle();
        hub.Connection.Should().BeEmpty();

        // (2) The client connects and calls JoinMatch — the hub looks
        //     up any buffered prompt for the new connection's sub.
        bridge.ReplayPromptIfAny(matchId, "creator-sub", "late-join-conn");

        hub.Connection.Should().ContainSingle();
        hub.Connection[0].ConnectionId.Should().Be("late-join-conn");
        hub.Connection[0].Event.Should().Be("prompt");
        hub.Connection[0].Payload.Should().BeSameAs(openingMulligan);
    }

    // -----------------------------------------------------------------------
    // Replay buffer wiring
    //
    // The bridge is the engine→hub seam; the replay buffer hangs off
    // ForwardEvent + Detach so the live broadcast and the replay log
    // share a single capture point. Tests here verify the side-channel
    // doesn't perturb the live broadcast and that Detach seals the log.
    // -----------------------------------------------------------------------

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; private set; } =
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private static MatchFacadeBridge BuildBridgeWithReplay(CaptureHub hub, out MatchReplayBuffer replay)
    {
        replay = new MatchReplayBuffer(new FixedClock(), NullLogger<MatchReplayBuffer>.Instance);
        return new MatchFacadeBridge(hub, NullLogger<MatchFacadeBridge>.Instance, replay);
    }

    [Fact]
    public void ForwardEvent_AlsoCapturesToReplayBuffer()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridgeWithReplay(hub, out var replay);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var evt = FakeEvent("CardDrawnEvent");
        var envelope = new EventEnvelope(evt, PerPlayer: null);

        bridge.ForwardEvent(matchId, envelope, DefaultRouting(aliceId, bobId));

        // Live broadcast unchanged — replay capture is a side-channel.
        hub.Group.Should().ContainSingle();
        hub.Group[0].Payload.Should().BeSameAs(evt);

        // And the same EventDto is now in the replay log.
        var dto = replay.GetReplay(matchId);
        dto.Should().NotBeNull();
        dto!.EntryCount.Should().Be(1);
        dto.Entries[0].Kind.Should().Be(ReplayEntry.KindEvent);
        dto.Entries[0].Event.Should().BeSameAs(evt);
        dto.SealedAt.Should().BeNull();
    }

    [Fact]
    public void Detach_SealsReplayBuffer()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridgeWithReplay(hub, out var replay);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var routing = DefaultRouting(aliceId, bobId);

        bridge.ForwardEvent(matchId, new EventEnvelope(FakeEvent("TurnStartedEvent"), PerPlayer: null), routing);
        replay.GetReplay(matchId)!.SealedAt.Should().BeNull();

        bridge.Detach(matchId);

        var dto = replay.GetReplay(matchId)!;
        dto.SealedAt.Should().NotBeNull("Detach seals the buffer so finished matches stop accepting writes");

        // Writes after Detach are dropped from the replay log.
        bridge.ForwardEvent(matchId, new EventEnvelope(FakeEvent("LateEvent"), PerPlayer: null), routing);
        replay.GetReplay(matchId)!.EntryCount.Should().Be(1);
    }

    [Fact]
    public void Bridge_WithoutReplayBuffer_StillWorks()
    {
        // Buffer is optional — old call sites (and tests) construct
        // the bridge without it. Live broadcast must continue.
        var hub = new CaptureHub();
        var bridge = new MatchFacadeBridge(hub, NullLogger<MatchFacadeBridge>.Instance, replay: null);
        var matchId = Guid.NewGuid();
        var routing = DefaultRouting(Guid.NewGuid(), Guid.NewGuid());

        var act = () =>
        {
            bridge.ForwardEvent(matchId, new EventEnvelope(FakeEvent("X"), PerPlayer: null), routing);
            bridge.Detach(matchId);
        };
        act.Should().NotThrow();
        hub.Group.Should().ContainSingle();
    }
}
