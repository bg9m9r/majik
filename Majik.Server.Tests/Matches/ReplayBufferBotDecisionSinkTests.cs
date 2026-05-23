using FluentAssertions;
using Majik.Bot.Diagnostics;
using Majik.Server.Matches;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Tests for <see cref="ReplayBufferBotDecisionSink"/>: the per-match
/// fan-out that lands bot decisions in the replay log alongside engine
/// events.
/// </summary>
public sealed class ReplayBufferBotDecisionSinkTests
{
    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; } =
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private static BotDecision Decision(string chosen = "Pass") => new(
        DecisionType: "Priority",
        Chosen: chosen,
        ChosenScore: 1.5,
        Alternatives: Array.Empty<BotDecisionAlternative>(),
        Context: new Dictionary<string, string> { ["mana"] = "RR" });

    [Fact]
    public void Record_AppendsToBuffer_ForCapturedMatchOnly()
    {
        var buffer = new MatchReplayBuffer(new FixedClock(), NullLogger<MatchReplayBuffer>.Instance);
        var matchA = Guid.NewGuid();
        var matchB = Guid.NewGuid();
        var sinkA = new ReplayBufferBotDecisionSink(matchA, buffer);

        sinkA.Record(Decision("CastSpell:Lightning Bolt"));
        sinkA.Record(Decision("Pass"));

        // The sink is scoped to matchA — matchB sees nothing.
        var a = buffer.GetReplay(matchA);
        a.Should().NotBeNull();
        a!.EntryCount.Should().Be(2);
        a.Entries.All(e => e.Kind == ReplayEntry.KindBotDecision).Should().BeTrue();
        a.Entries[0].Decision!.Chosen.Should().Be("CastSpell:Lightning Bolt");
        buffer.GetReplay(matchB).Should().BeNull();
    }

    [Fact]
    public void Ctor_NullBuffer_Throws()
    {
        var act = () => new ReplayBufferBotDecisionSink(Guid.NewGuid(), null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
