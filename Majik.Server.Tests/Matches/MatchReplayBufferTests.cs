using System.Text.Json;
using FluentAssertions;
using Majik.Bot.Diagnostics;
using Majik.Core.Api.Dtos;
using Majik.Server.Matches;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Unit tests for <see cref="MatchReplayBuffer"/>. The buffer is the
/// engine→hub side-channel capture that backs the
/// <c>GET /matches/{id}/replay</c> endpoint. Tests cover:
///   * arrival-order preservation across event + bot-decision interleaves
///   * sequence-number monotonicity
///   * Seal semantics (no further writes; finished marker)
///   * per-match cap + truncation flag
///   * LRU eviction of sealed buffers, never evicting active ones
/// </summary>
public sealed class MatchReplayBufferTests
{
    private sealed class FixedClock : IClock
    {
        private DateTime _now = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => _now;
        public void Advance(TimeSpan d) { _now = _now.Add(d); }
    }

    private static MatchReplayBuffer Build(out FixedClock clock)
    {
        clock = new FixedClock();
        return new MatchReplayBuffer(clock, NullLogger<MatchReplayBuffer>.Instance);
    }

    private static EventDto FakeEvent(string type) => new(
        EventId: Guid.NewGuid(),
        Type: type,
        At: DateTime.UtcNow,
        Payload: JsonDocument.Parse("""{"x":1}""").RootElement.Clone());

    private static BotDecision FakeDecision(string chosen = "Pass") => new(
        DecisionType: "Priority",
        Chosen: chosen,
        ChosenScore: 0.5,
        Alternatives: Array.Empty<BotDecisionAlternative>(),
        Context: new Dictionary<string, string>());

    [Fact]
    public void GetReplay_UnknownMatch_ReturnsNull()
    {
        var buffer = Build(out _);
        buffer.GetReplay(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void RecordEvent_AppendsInOrder()
    {
        var buffer = Build(out var clock);
        var matchId = Guid.NewGuid();

        buffer.RecordEvent(matchId, FakeEvent("A"));
        clock.Advance(TimeSpan.FromMilliseconds(5));
        buffer.RecordEvent(matchId, FakeEvent("B"));
        clock.Advance(TimeSpan.FromMilliseconds(5));
        buffer.RecordEvent(matchId, FakeEvent("C"));

        var dto = buffer.GetReplay(matchId);
        dto.Should().NotBeNull();
        dto!.MatchId.Should().Be(matchId);
        dto.SealedAt.Should().BeNull();
        dto.Truncated.Should().BeFalse();
        dto.EntryCount.Should().Be(3);
        dto.Entries.Select(e => e.Event!.Type).Should().Equal("A", "B", "C");
        dto.Entries.All(e => e.Kind == ReplayEntry.KindEvent).Should().BeTrue();
        // Seq strictly increases.
        dto.Entries.Select(e => e.Seq).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        // At reflects the clock at capture.
        dto.Entries[0].At.Should().BeBefore(dto.Entries[2].At);
    }

    [Fact]
    public void RecordDecision_AppendsInOrder_InterleavedWithEvents()
    {
        var buffer = Build(out _);
        var matchId = Guid.NewGuid();

        buffer.RecordEvent(matchId, FakeEvent("TurnStarted"));
        buffer.RecordDecision(matchId, FakeDecision("CastSpell:Lightning Bolt"));
        buffer.RecordEvent(matchId, FakeEvent("SpellCast"));
        buffer.RecordDecision(matchId, FakeDecision("Pass"));

        var dto = buffer.GetReplay(matchId)!;
        dto.EntryCount.Should().Be(4);
        dto.Entries.Select(e => e.Kind).Should().Equal(
            ReplayEntry.KindEvent,
            ReplayEntry.KindBotDecision,
            ReplayEntry.KindEvent,
            ReplayEntry.KindBotDecision);
        dto.Entries[1].Decision!.Chosen.Should().Be("CastSpell:Lightning Bolt");
        // The Event-kind entries carry no Decision, and vice versa.
        dto.Entries[0].Event.Should().NotBeNull();
        dto.Entries[0].Decision.Should().BeNull();
        dto.Entries[1].Event.Should().BeNull();
        dto.Entries[1].Decision.Should().NotBeNull();
    }

    [Fact]
    public void Seq_IsGloballyMonotonic_AcrossMatches()
    {
        var buffer = Build(out _);
        var matchA = Guid.NewGuid();
        var matchB = Guid.NewGuid();

        buffer.RecordEvent(matchA, FakeEvent("A1"));
        buffer.RecordEvent(matchB, FakeEvent("B1"));
        buffer.RecordEvent(matchA, FakeEvent("A2"));

        var seqA = buffer.GetReplay(matchA)!.Entries.Select(e => e.Seq).ToArray();
        var seqB = buffer.GetReplay(matchB)!.Entries.Select(e => e.Seq).ToArray();
        // Within each match the seqs are monotonic; across both they are
        // strictly unique so external mergers can recover global order.
        var all = seqA.Concat(seqB).ToArray();
        all.Should().OnlyHaveUniqueItems();
        seqA[0].Should().BeLessThan(seqB[0]);
        seqB[0].Should().BeLessThan(seqA[1]);
    }

    [Fact]
    public void Seal_StampsSealedAt_AndIgnoresFurtherWrites()
    {
        var buffer = Build(out var clock);
        var matchId = Guid.NewGuid();
        buffer.RecordEvent(matchId, FakeEvent("A"));
        clock.Advance(TimeSpan.FromMilliseconds(10));
        var sealMoment = clock.UtcNow;

        buffer.Seal(matchId);

        // Writes after Seal are dropped — finished matches don't grow.
        buffer.RecordEvent(matchId, FakeEvent("B"));
        buffer.RecordDecision(matchId, FakeDecision());

        var dto = buffer.GetReplay(matchId)!;
        dto.EntryCount.Should().Be(1);
        dto.SealedAt.Should().Be(sealMoment);
        dto.Truncated.Should().BeFalse();
    }

    [Fact]
    public void Seal_UnknownMatch_IsNoop()
    {
        var buffer = Build(out _);
        // No throw — Detach funnels every terminal-state path through
        // Seal, including matches that never recorded an event.
        var act = () => buffer.Seal(Guid.NewGuid());
        act.Should().NotThrow();
        buffer.BufferCount.Should().Be(0);
    }

    [Fact]
    public void PerMatchCap_TruncatesAndFlagsOverflow()
    {
        var buffer = Build(out _);
        var matchId = Guid.NewGuid();
        // Push past the cap. We don't want a 20k+ allocation in the test
        // suite, so we just verify the flag rather than walking up to the
        // real MaxEntriesPerMatch constant — that's covered implicitly by
        // the unbounded test above. Here, hit the boundary via a tight
        // loop and assert nothing leaks past the cap.
        for (int i = 0; i < MatchReplayBuffer.MaxEntriesPerMatch + 5; i++)
        {
            buffer.RecordEvent(matchId, FakeEvent("E"));
        }
        var dto = buffer.GetReplay(matchId)!;
        dto.EntryCount.Should().Be(MatchReplayBuffer.MaxEntriesPerMatch);
        dto.Truncated.Should().BeTrue();
    }

    [Fact]
    public void EvictIfOver_EvictsOldestSealed_RetainsActive()
    {
        var buffer = Build(out var clock);

        // Active match — must never be evicted.
        var active = Guid.NewGuid();
        buffer.RecordEvent(active, FakeEvent("live"));

        // Seal MaxRetainedMatches buffers in chronological order, then
        // seal one more — the OLDEST sealed entry should be the eviction
        // victim, NOT the active one.
        var sealed_ = new List<Guid>();
        for (int i = 0; i < MatchReplayBuffer.MaxRetainedMatches; i++)
        {
            var id = Guid.NewGuid();
            buffer.RecordEvent(id, FakeEvent("x"));
            clock.Advance(TimeSpan.FromMilliseconds(1));
            buffer.Seal(id);
            sealed_.Add(id);
        }
        buffer.BufferCount.Should().Be(MatchReplayBuffer.MaxRetainedMatches + 1);

        // One more sealed buffer pushes past the cap.
        var fresh = Guid.NewGuid();
        buffer.RecordEvent(fresh, FakeEvent("y"));
        clock.Advance(TimeSpan.FromMilliseconds(1));
        buffer.Seal(fresh);

        // The oldest sealed buffer is gone; the active one survives.
        buffer.GetReplay(active).Should().NotBeNull("active matches are never evicted");
        buffer.GetReplay(sealed_[0]).Should().BeNull("oldest sealed buffer is the eviction victim");
        buffer.GetReplay(fresh).Should().NotBeNull();
    }
}
