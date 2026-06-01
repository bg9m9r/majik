using System.Text.Json;
using FluentAssertions;
using Majik.Core.Api.Dtos;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Robustness Slice 0 safety-net invariants, driven through
/// <see cref="LayerAgreementHarness"/>. These assert the three load-bearing
/// agreements between the engine, the server clock, and the wire contract:
///   1. clock holder ↔ engine active player (CR 117 / 103.7),
///   2. phase vocabulary is always disambiguated (never raw "Main"),
///   3. seat identity (Creator → Alice).
/// </summary>
public class LayerAgreementInvariantTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public LayerAgreementInvariantTests(TestMongoFixture fixture) => _fixture = fixture;

    // -----------------------------------------------------------------------
    // Task 2 — smoke test: bot match reaches Playing + a clock-update is seen.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BotMatch_ReachesPlaying_AndPublishesClockUpdate()
    {
        using var h = await LayerAgreementHarness.StartBotMatchAsync(_fixture, rngSeed: 1);

        // Facade exists and is live (active player resolvable).
        h.Facade.Should().NotBeNull();
        h.Facade.ActivePlayerId.Should().NotBe(System.Guid.Empty);

        // PlayDrawAsync publishes the opening match.clock-update on the
        // Rolling → Playing transition.
        h.Published.Snapshot().Select(e => e.@event)
            .Should().Contain("match.clock-update");
    }

    // -----------------------------------------------------------------------
    // Task 3 — phase vocabulary: every step/phase string is disambiguated.
    //
    // Step / phase info reaches the wire on the "event" channel as an
    // EventDto whose Type is StepStartedEvent / PhaseStartedEvent and whose
    // serialized payload carries the disambiguated label in `step` / `phase`
    // (EventPayloadBuilder runs it through PhaseLabelResolver). The raw
    // "Main" label must never escape — it is split into PreCombatMain /
    // PostCombatMain per CR 505.
    // -----------------------------------------------------------------------

    private static readonly HashSet<string> AllowedPhaseVocabulary = new()
    {
        "Untap", "Upkeep", "Draw", "PreCombatMain", "BeginningOfCombat",
        "DeclareAttackers", "DeclareBlockers", "CombatDamage", "EndOfCombat",
        "PostCombatMain", "End", "Cleanup",
    };

    [Fact]
    public async Task PhaseVocabulary_NeverRawMain_AlwaysDisambiguated()
    {
        using var h = await LayerAgreementHarness.StartBotMatchAsync(_fixture, rngSeed: 7);

        // Advance ~12 passes from the human seat to walk the engine through
        // several steps/phases. The bot seat self-drives (it has no /commands
        // surface), so only the creator passes are POSTed.
        for (var i = 0; i < 12; i++)
        {
            await h.AdvanceByPassAsync(h.CreatorSub);
        }

        var labels = ExtractPhaseLabels(h);
        labels.Should().NotBeEmpty(
            "advancing the round must surface at least one step/phase event");

        foreach (var label in labels)
        {
            label.Should().NotBe("Main",
                "the raw CR 505 'Main' label must be disambiguated into " +
                "PreCombatMain / PostCombatMain before it reaches the wire");
            AllowedPhaseVocabulary.Should().Contain(label,
                $"'{label}' is not part of the authoritative phase/step vocabulary");
        }
    }

    // -----------------------------------------------------------------------
    // Task 4 — clock ↔ priority: the latest clock-update holder maps to the
    // engine's current active player (CR 117 / 103.7). RED until the Slice 1
    // fix wires the clock handoff: PlayDrawAsync sets the holder once and
    // freezes it, so without the fix the holder stays the first player even
    // after the turn passes to the other seat.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ClockHolder_FollowsEngineActivePlayer_AcrossTurnHandoff()
    {
        using var h = await LayerAgreementHarness.StartBotMatchAsync(_fixture, rngSeed: 11);

        var initialActive = h.Facade.ActivePlayerId;

        // Snapshot the fire count BEFORE the genuine handoff. After the I2
        // fix the turn-1 active-player observation only seeds the tracker (no
        // handoff), so this baseline should be 0 — but snapshotting makes the
        // assertion robust regardless: we require the count to INCREASE across
        // the real seat change, so it can't be satisfied by a spurious fire
        // that happened before the turn actually flipped (I3).
        var fireCountBeforeHandoff = h.Bridge.ClockHandoffFireCount;

        // Advance creator passes until the engine's active player changes
        // seats (a turn handoff), capped at 40 passes.
        var handoffSeen = false;
        for (var i = 0; i < 40 && !handoffSeen; i++)
        {
            await h.AdvanceByPassAsync(h.CreatorSub);
            if (h.Facade.ActivePlayerId != initialActive) handoffSeen = true;
        }

        handoffSeen.Should().BeTrue(
            "advancing ~40 passes must hand the turn to the other seat at least once");
        h.Bridge.ClockHandoffFireCount.Should().BeGreaterThan(fireCountBeforeHandoff,
            "the bridge must have fired the clock-handoff callback ACROSS the genuine " +
            "turn handoff (count increased over the pre-handoff baseline) — the clock " +
            "following the engine can't be a coincidence or a turn-1 spurious fire");

        // The clock-handoff callback fires asynchronously (it opens a DI
        // scope + Mongo round-trip off the engine's synchronous event
        // dispatch), so the match.clock-update is eventually consistent with
        // the engine. Poll briefly for the latest holder to converge onto
        // the active player. Creator → Alice, Bot/Opponent → Bob.
        var expectedActive = h.Facade.ActivePlayerId;
        string? holder = null;
        Guid holderSeatId = System.Guid.Empty;
        // Deadline-based poll (was a fixed 50 × 20 ms = 1 s budget, which
        // flaked when the async clock-handoff round-trip ran slow under CI
        // load). The loop still exits the instant the holder converges, so the
        // happy path is unchanged; only the worst-case ceiling is generous.
        var convergeDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < convergeDeadline)
        {
            holder = h.LatestClockHolderSub();
            if (holder != null)
            {
                holderSeatId = holder == h.CreatorSub ? h.Facade.Alice.Id : h.Facade.Bob.Id;
                // Re-read the active player in case the engine advanced
                // another turn while we were polling.
                expectedActive = h.Facade.ActivePlayerId;
                if (holderSeatId == expectedActive) break;
            }
            await Task.Delay(20);
        }

        holder.Should().NotBeNull("a clock-update must have been published");
        holderSeatId.Should().Be(expectedActive,
            "the clock holder must reflect the engine's active player — " +
            "the server clock DERIVES the holder from the engine, never freezes it");
    }

    // -----------------------------------------------------------------------
    // Task 5 — seat identity: a prompt for the creator carries
    // playerId == Alice.Id (Creator → Alice convention). The portal reads
    // PromptDto.PlayerId to decide which seat must act; it must DERIVE seat
    // identity from the engine's id, never recompute it from sub strings.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SeatIdentity_CreatorPrompt_CarriesAliceId()
    {
        using var h = await LayerAgreementHarness.StartBotMatchAsync(_fixture, rngSeed: 3);

        // One pass surfaces a fresh PassPriorityCommand prompt for the
        // creator's seat (the engine awaits the human after walking a step).
        await h.AdvanceByPassAsync(h.CreatorSub);

        // The bridge fans prompts per-recipient; the harness records each as
        // a "prompt" entry carrying the PromptDto. Find any prompt addressed
        // to the creator (Alice) and assert its PlayerId is Alice's engine id.
        var creatorPrompts = h.Published.Snapshot()
            .Where(e => e.@event == "prompt" && e.payload is Majik.Core.Api.Dtos.PromptDto pd
                        && pd.PlayerId == h.Facade.Alice.Id)
            .ToList();

        creatorPrompts.Should().NotBeEmpty(
            "the engine must prompt the creator's seat, and that prompt must " +
            "carry Alice's engine id (Creator → Alice convention)");

        // And the reflection helper reads the same id off the payload — the
        // seam the portal relies on when it can only see the wire shape.
        var first = (Majik.Core.Api.Dtos.PromptDto)creatorPrompts[0].payload;
        PayloadReflection.GetString(first, "PlayerId")
            .Should().Be(h.Facade.Alice.Id.ToString());
    }

    // -----------------------------------------------------------------------
    // Task 6 (Slice 2a) — YouPlayerId: GET /state as creator returns a
    // GameStateDto whose YouPlayerId equals Alice.Id (Creator → Alice).
    // The portal reads this field to self-identify its seat without a second
    // round-trip — it must be non-null and correct for every seated viewer.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task YouPlayerId_CreatorGetState_EqualsAliceId()
    {
        using var h = await LayerAgreementHarness.StartBotMatchAsync(_fixture, rngSeed: 5);

        var state = await h.GetStateAsync(h.CreatorSub);

        state.Should().NotBeNull(
            "GET /state as the creator must return a 200 GameStateDto");
        state!.YouPlayerId.Should().Be(h.Facade.Alice.Id,
            "Creator → Alice convention: YouPlayerId on the creator's per-viewer " +
            "snapshot must equal Alice's engine seat id (Slice 2a invariant)");
    }

    // -----------------------------------------------------------------------
    // Slice 4b #1 — snapshot-on-join: a viewer who joins AFTER the engine has
    // already emitted events (opening draws, mulligan) recovers authoritative
    // state. The bridge pushes the per-viewer GameStateDto to the joining
    // connection on the "state" channel, masked per CR 706.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SnapshotOnJoin_AfterEngineEmittedEvents_PushesMaskedPerViewerSnapshot()
    {
        using var h = await LayerAgreementHarness.StartBotMatchAsync(_fixture, rngSeed: 13);

        // The engine has already started, dealt opening hands, and walked the
        // opening mulligan (KeepOpeningHandAsync in the harness setup). Advance
        // a few more passes so additional events fire — the "late join" client
        // missed all of these.
        for (var i = 0; i < 4; i++)
        {
            await h.AdvanceByPassAsync(h.CreatorSub);
        }

        // Simulate the creator's SignalR connection joining the match group
        // AFTER all those events. JoinMatch wires this exact call.
        h.Bridge.ReplaySnapshotIfAny(h.MatchId, h.CreatorSub, "late-join-conn");

        var sends = h.Published.ConnectionSnapshot();
        var stateSend = sends.LastOrDefault(s => s.connectionId == "late-join-conn" && s.@event == "state");
        stateSend.payload.Should().NotBeNull(
            "a late-joining connection must receive a full per-viewer snapshot on the " +
            "'state' channel regardless of which early events it missed");

        var snapshot = stateSend.payload.Should().BeOfType<GameStateDto>().Subject;

        // Per-viewer (Creator → Alice): the snapshot self-identifies as Alice.
        snapshot.YouPlayerId.Should().Be(h.Facade.Alice.Id,
            "the snapshot must be the per-viewer (GetStateFor) variant, never the " +
            "full-reveal spectator view (YouPlayerId null)");

        // CR 706: the BOT opponent's (Bob's) hand must be masked in the
        // creator's view. Libraries are always masked even for the owner.
        var bobView = snapshot.Players.Single(p => p.Id == h.Facade.Bob.Id);
        bobView.Hand.Cards.Should().OnlyContain(c => c.Name == "(hidden)",
            "CR 706 — the joining viewer must never see the opponent's hand card names");
        foreach (var player in snapshot.Players)
        {
            player.Library.Cards.Should().OnlyContain(c => c.Name == "(hidden)",
                "CR 706 — libraries are always hidden, even the viewer's own");
        }
    }

    private static List<string> ExtractPhaseLabels(LayerAgreementHarness h)
    {
        var labels = new List<string>();
        foreach (var (_, @event, payload) in h.Published.Snapshot())
        {
            if (@event != "event") continue;
            if (payload is not EventDto dto) continue;
            if (dto.Type == "StepStartedEvent" || dto.Type == "StepEndedEvent")
            {
                if (TryReadString(dto.Payload, "step", out var step)) labels.Add(step);
            }
            else if (dto.Type == "PhaseStartedEvent" || dto.Type == "PhaseEndedEvent")
            {
                if (TryReadString(dto.Payload, "phase", out var phase)) labels.Add(phase);
            }
        }
        return labels;
    }

    private static bool TryReadString(JsonElement payload, string member, out string value)
    {
        value = string.Empty;
        if (payload.ValueKind != JsonValueKind.Object) return false;
        if (!payload.TryGetProperty(member, out var prop)) return false;
        if (prop.ValueKind != JsonValueKind.String) return false;
        value = prop.GetString() ?? string.Empty;
        return value.Length > 0;
    }
}
