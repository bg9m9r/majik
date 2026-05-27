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
