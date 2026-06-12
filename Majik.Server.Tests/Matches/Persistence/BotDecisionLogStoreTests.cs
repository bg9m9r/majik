using FluentAssertions;
using Majik.Core.Api.BotReplay;
using Majik.Server.Matches.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace Majik.Server.Tests.Matches.Persistence;

/// <summary>
/// Bot-decision persistence — the durable, ordered, idempotent per-match bot
/// answer log (parallel to <see cref="IEngineCommandLogStore"/>), owned by
/// <see cref="EnginePersistenceCoordinator"/> behind the same
/// <see cref="EnginePersistenceOptions.Enabled"/> gate.
/// </summary>
public class BotDecisionLogStoreTests
{
    [Fact]
    public async Task Append_IsIdempotentOn_MatchId_BotSeq()
    {
        var store = new InMemoryBotDecisionLogStore();
        var matchId = Guid.NewGuid();

        await store.AppendAsync(matchId,
            new BotDecisionRecord(0, BotDecisionKind.YesNo, new YesNoPayload(true)), default);
        // Duplicate (matchId, botSeq) — the FIRST write stands.
        await store.AppendAsync(matchId,
            new BotDecisionRecord(0, BotDecisionKind.YesNo, new YesNoPayload(false)), default);

        var all = await store.ReadAllAsync(matchId, default);
        all.Should().HaveCount(1, "a duplicate (matchId, botSeq) append must not add a second entry");
        all[0].Payload.Should().BeOfType<YesNoPayload>().Which.Answer.Should().BeTrue(
            "the FIRST write stands");
    }

    [Fact]
    public async Task ReadAll_ReturnsRecordsOrderedByBotSeq()
    {
        var store = new InMemoryBotDecisionLogStore();
        var matchId = Guid.NewGuid();

        // Append out of order — reads must still come back ordered.
        await store.AppendAsync(matchId,
            new BotDecisionRecord(2, BotDecisionKind.X, new XPayload(2)), default);
        await store.AppendAsync(matchId,
            new BotDecisionRecord(0, BotDecisionKind.X, new XPayload(0)), default);
        await store.AppendAsync(matchId,
            new BotDecisionRecord(1, BotDecisionKind.X, new XPayload(1)), default);

        var all = await store.ReadAllAsync(matchId, default);
        all.Select(r => r.BotSeq).Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task ReadAll_UnknownMatch_ReturnsEmpty()
    {
        var store = new InMemoryBotDecisionLogStore();
        var all = await store.ReadAllAsync(Guid.NewGuid(), default);
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAll_IsolatesMatches()
    {
        var store = new InMemoryBotDecisionLogStore();
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();

        await store.AppendAsync(m1,
            new BotDecisionRecord(0, BotDecisionKind.X, new XPayload(1)), default);
        await store.AppendAsync(m2,
            new BotDecisionRecord(0, BotDecisionKind.X, new XPayload(2)), default);

        (await store.ReadAllAsync(m1, default)).Should().ContainSingle()
            .Which.Payload.Should().BeOfType<XPayload>().Which.X.Should().Be(1);
        (await store.ReadAllAsync(m2, default)).Should().ContainSingle()
            .Which.Payload.Should().BeOfType<XPayload>().Which.X.Should().Be(2);
    }

    // ── Coordinator gating ──────────────────────────────────────────────────

    [Fact]
    public async Task Coordinator_FlagOff_RecordBotDecision_WritesNothing_AndReadReturnsEmpty()
    {
        var store = new CountingBotDecisionStore();
        var coord = BuildCoordinator(store, enabled: false);

        await coord.RecordBotDecisionAsync(Guid.NewGuid(),
            new BotDecisionRecord(0, BotDecisionKind.YesNo, new YesNoPayload(true)), default);
        store.Appends.Should().Be(0, "flag off → no durable bot-decision writes");

        var read = await coord.ReadBotDecisionsAsync(Guid.NewGuid(), default);
        read.Should().BeEmpty("flag off → nothing is ever read back");
        store.Reads.Should().Be(0, "flag off → the store is never consulted");
    }

    [Fact]
    public async Task Coordinator_FlagOn_RecordsAndReadsBack()
    {
        var store = new InMemoryBotDecisionLogStore();
        var coord = BuildCoordinator(store, enabled: true);
        var matchId = Guid.NewGuid();

        await coord.RecordBotDecisionAsync(matchId,
            new BotDecisionRecord(0, BotDecisionKind.X, new XPayload(3)), default);
        await coord.RecordBotDecisionAsync(matchId,
            new BotDecisionRecord(1, BotDecisionKind.YesNo, new YesNoPayload(true)), default);

        var read = await coord.ReadBotDecisionsAsync(matchId, default);
        read.Select(r => r.BotSeq).Should().Equal(0, 1);
        read[0].Payload.Should().BeOfType<XPayload>().Which.X.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static EnginePersistenceCoordinator BuildCoordinator(
        IBotDecisionLogStore botDecisions, bool enabled) =>
        new(new InMemoryEngineCommandLogStore(),
            new InMemoryEngineCheckpointStore(),
            Options.Create(new EnginePersistenceOptions { Enabled = enabled }),
            botDecisions: botDecisions);

    private sealed class CountingBotDecisionStore : IBotDecisionLogStore
    {
        public int Appends { get; private set; }
        public int Reads { get; private set; }

        public Task AppendAsync(Guid matchId, BotDecisionRecord record, CancellationToken ct)
        {
            Appends++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BotDecisionRecord>> ReadAllAsync(Guid matchId, CancellationToken ct)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<BotDecisionRecord>>(Array.Empty<BotDecisionRecord>());
        }
    }
}
