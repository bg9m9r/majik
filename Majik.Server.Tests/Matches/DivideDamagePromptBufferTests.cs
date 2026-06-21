using System.Text.Json;
using FluentAssertions;
using Majik.Core.Api.Dtos;
using Majik.Server.Matches;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// SERVER-side coverage for the divide-damage human-seat prompt
/// (CR 601.2d / CR 119.4) — the residual the deferral
/// <c>divide-damage-trigger-portal-numeric-allocation-server-fallback-audit</c>
/// called out. The engine + contract layer shipped earlier (DamageDivisionViewDto
/// / DamageDivisionTargetDto / ChooseDamageDivisionCommand), and
/// <see cref="Majik.Core.Api.RemoteAgent.ChooseDamageDivisionAsync"/> RAISES the
/// prompt for a human seat, but there was no SERVER.Tests assertion that the
/// <see cref="DamageDivisionViewDto"/> actually reaches the server-side prompt
/// BUFFER (the <see cref="MatchFacadeBridge"/> per-recipient buffer that
/// persists + replays the most-recent prompt to a late-joining / reconnecting
/// human client) intact.
///
/// <para>These tests drive a <see cref="PromptDto"/> shaped exactly as the
/// engine emits for a human-seat Inferno Titan / Fury / Avacyn's Judgment
/// divide-damage trigger (one labelled row per chosen target) through the live
/// <see cref="MatchFacadeBridge.ForwardPrompt"/> path and assert:
/// <list type="number">
///   <item>the view is published per-recipient to the controlling HUMAN seat
///         only (never the opponent), with every target row intact;</item>
///   <item>the view survives the late-join buffer (so a reconnecting client
///         replays the numeric allocation prompt, not a dead "no active
///         prompt" state);</item>
///   <item>the prompt serializes to the camelCase wire shape the portal
///         consumes (<c>damageDivisionView</c>);</item>
///   <item>a BOT seat's divide-damage prompt is NOT buffered (the even-split
///         remains the bot / disconnected default — the deferral's explicit
///         invariant).</item>
/// </list></para>
/// </summary>
public sealed class DivideDamagePromptBufferTests
{
    private sealed record UserCall(Guid MatchId, string Event, string RecipientSub, object Payload);

    private sealed class CaptureHub : IMatchHubPublisher
    {
        public List<UserCall> PerRecipient { get; } = new();
        public int GroupCalls { get; private set; }
        public int ConnectionCalls { get; private set; }

        public void Publish(Guid matchId, string @event, object payload) => GroupCalls++;

        public void PublishPerRecipient(
            Guid matchId, string @event,
            IReadOnlyList<string> recipientSubs, Func<string, object> payloadFor)
        {
            foreach (var sub in recipientSubs)
            {
                if (string.IsNullOrEmpty(sub)) continue;
                PerRecipient.Add(new UserCall(matchId, @event, sub, payloadFor(sub)));
            }
        }

        public void SendToConnection(string connectionId, string @event, object payload) =>
            ConnectionCalls++;
    }

    // Inferno Titan: "deals 3 damage divided as you choose among one, two, or
    // three targets". A two-target human-seat prompt (Bob + a creature).
    private static PromptDto DivideDamagePrompt(Guid aliceId, Guid bobId, Guid bearId) =>
        new(
            GameId: Guid.NewGuid(),
            PlayerId: aliceId,
            ExpectedKinds: new[] { "ChooseDamageDivisionCommand" },
            DamageDivisionView: new DamageDivisionViewDto(
                SourceCardName: "Inferno Titan",
                TotalDamage: 3,
                Targets: new[]
                {
                    new DamageDivisionTargetDto(bobId, "Bob"),
                    new DamageDivisionTargetDto(bearId, "Grizzly Bears"),
                }));

    private static MatchFacadeBridge BuildBridge(CaptureHub hub) =>
        new(hub, NullLogger<MatchFacadeBridge>.Instance);

    [Fact]
    public void ForwardPrompt_DamageDivisionView_GoesOnlyToControllingHuman_Intact()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var bearId = Guid.NewGuid();
        var routing = new MatchFacadeBridge.PromptRouting(
            aliceId, bobId, "creator-sub", "opponent-sub");

        var prompt = DivideDamagePrompt(aliceId, bobId, bearId);
        bridge.ForwardPrompt(matchId, prompt, routing);

        hub.PerRecipient.Should().ContainSingle("the divide-damage prompt routes to the controller only");
        var call = hub.PerRecipient[0];
        call.Event.Should().Be("prompt");
        call.RecipientSub.Should().Be("creator-sub", "Alice (the human controller) gets the prompt");

        var delivered = call.Payload.Should().BeOfType<PromptDto>().Subject;
        delivered.ExpectedKinds.Should().Contain("ChooseDamageDivisionCommand");
        delivered.DamageDivisionView.Should().NotBeNull(
            "the numeric allocation view must survive the bridge fan-out intact");
        delivered.DamageDivisionView!.SourceCardName.Should().Be("Inferno Titan");
        delivered.DamageDivisionView.TotalDamage.Should().Be(3);
        delivered.DamageDivisionView.Targets.Should().HaveCount(2);
        delivered.DamageDivisionView.Targets.Select(t => t.TargetId)
            .Should().Contain(new[] { bobId, bearId });

        hub.GroupCalls.Should().Be(0, "a per-recipient prompt must not fan out to the match group");
    }

    [Fact]
    public void ForwardPrompt_DamageDivisionView_BuffersForLateJoinReplay_Intact()
    {
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var bearId = Guid.NewGuid();
        var routing = new MatchFacadeBridge.PromptRouting(
            aliceId, bobId, "creator-sub", "opponent-sub");

        var prompt = DivideDamagePrompt(aliceId, bobId, bearId);
        bridge.ForwardPrompt(matchId, prompt, routing);

        bridge.BufferedPromptCount.Should().Be(1, "the human-seat prompt must be buffered for replay");
        var buffered = bridge.PeekBufferedPrompt(matchId, "creator-sub");
        buffered.Should().NotBeNull("a reconnecting human replays the outstanding divide-damage prompt");
        buffered!.DamageDivisionView.Should().NotBeNull(
            "the buffered prompt must keep the numeric allocation view (no 'no active prompt' wedge on reconnect)");
        buffered.DamageDivisionView!.Targets.Should().HaveCount(2);

        // Opponent has nothing buffered.
        bridge.PeekBufferedPrompt(matchId, "opponent-sub").Should().BeNull();
    }

    [Fact]
    public void DamageDivisionPrompt_SerializesToCamelCaseWireShape()
    {
        var prompt = DivideDamagePrompt(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(prompt, opts);

        json.Should().Contain("\"damageDivisionView\"", "the portal consumes the camelCase field");
        json.Should().Contain("\"sourceCardName\":\"Inferno Titan\"");
        json.Should().Contain("\"totalDamage\":3");
        json.Should().Contain("\"name\":\"Grizzly Bears\"");
        json.Should().Contain("ChooseDamageDivisionCommand", "the expected-kinds list rides the wire");
    }

    [Fact]
    public void ForwardPrompt_BotSeatDivideDamage_IsNotBuffered()
    {
        // The even-split remains the bot / disconnected default (the deferral's
        // explicit invariant). Bot seats run in-process with no SignalR
        // connection to late-join, so their prompts are never buffered — even a
        // divide-damage one. (In production the bot agent never even raises this
        // prompt; this guards the server buffer's bot-skip regardless.)
        var hub = new CaptureHub();
        var bridge = BuildBridge(hub);
        var matchId = Guid.NewGuid();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var bearId = Guid.NewGuid();
        var routing = new MatchFacadeBridge.PromptRouting(
            aliceId, bobId, "creator-sub", "bot:aggro");

        // The bot (Bob) is the one being prompted.
        var prompt = DivideDamagePrompt(bobId, aliceId, bearId);
        bridge.ForwardPrompt(matchId, prompt, routing);

        bridge.BufferedPromptCount.Should().Be(0, "bot seats are never buffered");
    }
}
