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
        h.Bridge.ClockHandoffFireCount.Should().BeGreaterThan(0,
            "the bridge must have fired the clock-handoff callback on the turn " +
            "handoff — the clock following the engine can't be a coincidence");

        // The clock-handoff callback fires asynchronously (it opens a DI
        // scope + Mongo round-trip off the engine's synchronous event
        // dispatch), so the match.clock-update is eventually consistent with
        // the engine. Poll briefly for the latest holder to converge onto
        // the active player. Creator → Alice, Bot/Opponent → Bob.
        var expectedActive = h.Facade.ActivePlayerId;
        string? holder = null;
        Guid holderSeatId = System.Guid.Empty;
        for (var i = 0; i < 50; i++)
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
