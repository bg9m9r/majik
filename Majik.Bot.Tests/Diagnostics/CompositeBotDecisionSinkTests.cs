using FluentAssertions;
using Majik.Bot.Diagnostics;
using Xunit;

namespace Majik.Bot.Tests.Diagnostics;

/// <summary>
/// Unit tests for <see cref="CompositeBotDecisionSink"/> — the fan-out
/// sink that lets the server send each <see cref="BotDecision"/> to both
/// the stdout logger AND the per-match SignalR publisher.
/// </summary>
public class CompositeBotDecisionSinkTests
{
    private sealed class CountingSink : IBotDecisionSink
    {
        public int Calls { get; private set; }
        public BotDecision? Last { get; private set; }
        public void Record(BotDecision decision)
        {
            Calls++;
            Last = decision;
        }
    }

    private sealed class ThrowingSink : IBotDecisionSink
    {
        public int Calls { get; private set; }
        public void Record(BotDecision decision)
        {
            Calls++;
            throw new InvalidOperationException("simulated observer fault");
        }
    }

    private static BotDecision MakeDecision() => new(
        DecisionType: "Priority",
        Chosen: "Pass",
        ChosenScore: 0.0,
        Alternatives: Array.Empty<BotDecisionAlternative>(),
        Context: new Dictionary<string, string>());

    [Fact]
    public void Compose_AllNull_ReturnsNullSinkInstance()
    {
        // Sentinel collapse: callers can blindly compose any combination
        // and check against NullBotDecisionSink.Instance to decide whether
        // to set BotConfig.DecisionSink at all.
        var sink = CompositeBotDecisionSink.Compose(null, null);
        sink.Should().BeSameAs(NullBotDecisionSink.Instance);
    }

    [Fact]
    public void Compose_AllNullSinkInstances_CollapsesToNullSink()
    {
        var sink = CompositeBotDecisionSink.Compose(
            NullBotDecisionSink.Instance,
            NullBotDecisionSink.Instance);
        sink.Should().BeSameAs(NullBotDecisionSink.Instance);
    }

    [Fact]
    public void Compose_OnlyOneRealSink_ReturnsItDirectly_NoWrapper()
    {
        // No point wrapping a single sink — saves an allocation per
        // decision and one extra try/catch.
        var inner = new CountingSink();
        var sink = CompositeBotDecisionSink.Compose(inner, null);
        sink.Should().BeSameAs(inner);
    }

    [Fact]
    public void Compose_TwoRealSinks_FansOutToBoth()
    {
        var a = new CountingSink();
        var b = new CountingSink();
        var sink = CompositeBotDecisionSink.Compose(a, b);

        var d = MakeDecision();
        sink.Record(d);

        a.Calls.Should().Be(1);
        b.Calls.Should().Be(1);
        a.Last.Should().BeSameAs(d);
        b.Last.Should().BeSameAs(d);
    }

    [Fact]
    public void Compose_DropsNullSinkInstances_FromFanOut()
    {
        var real = new CountingSink();
        var sink = CompositeBotDecisionSink.Compose(NullBotDecisionSink.Instance, real, null);
        // Either real returned directly (single-survivor path) or wrapped;
        // either way Record must reach the real sink exactly once.
        sink.Record(MakeDecision());
        real.Calls.Should().Be(1);
    }

    [Fact]
    public void Record_OneSinkThrows_OtherSinksStillCalled()
    {
        var faulty = new ThrowingSink();
        var healthy = new CountingSink();
        var sink = CompositeBotDecisionSink.Compose(faulty, healthy);

        // Must not propagate — the observer contract is "faulty sink must
        // not abort the engine".
        var act = () => sink.Record(MakeDecision());
        act.Should().NotThrow();

        faulty.Calls.Should().Be(1);
        healthy.Calls.Should().Be(1);
    }
}
