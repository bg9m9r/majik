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

    private sealed class CaptureHub : IMatchHubPublisher
    {
        public List<GroupCall> Group { get; } = new();
        public List<UserCall> PerRecipient { get; } = new();

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
    }

    private static MatchFacadeBridge BuildBridge(CaptureHub hub) =>
        new MatchFacadeBridge(hub, NullLogger<MatchFacadeBridge>.Instance);

    private static EventDto FakeEvent(string type = "TurnStartedEvent") => new(
        EventId: Guid.NewGuid(),
        Type: type,
        At: DateTime.UtcNow,
        Payload: JsonDocument.Parse("""{"hello":"world"}""").RootElement.Clone());

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
        var evt = FakeEvent("CardDrawnEvent");

        bridge.ForwardEvent(matchId, evt);

        hub.Group.Should().ContainSingle();
        hub.Group[0].MatchId.Should().Be(matchId);
        hub.Group[0].Event.Should().Be("event");
        hub.Group[0].Payload.Should().BeSameAs(evt);
        // Group fan-out goes to ALL connections in the match group —
        // there's no per-viewer masking on this channel.
        hub.PerRecipient.Should().BeEmpty();
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
}
