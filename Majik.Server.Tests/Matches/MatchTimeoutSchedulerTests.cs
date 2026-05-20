using FluentAssertions;
using Majik.Server.Matches;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class MatchTimeoutSchedulerTests
{
    [Fact]
    public async Task Schedule_FiresAfterRemainingElapses()
    {
        var fired = new TaskCompletionSource<(Guid, string)>();
        var scheduler = new MatchTimeoutScheduler((matchId, holder, ct) =>
        {
            fired.TrySetResult((matchId, holder));
            return Task.CompletedTask;
        });
        var matchId = Guid.NewGuid();

        scheduler.Schedule(matchId, "stub-alice", 80);
        var got = await fired.Task.WaitAsync(TimeSpan.FromSeconds(2));

        got.Item1.Should().Be(matchId);
        got.Item2.Should().Be("stub-alice");
    }

    [Fact]
    public async Task Schedule_TwiceCancelsFirst()
    {
        var fired = new List<string>();
        var scheduler = new MatchTimeoutScheduler((id, holder, ct) =>
        {
            lock (fired) fired.Add(holder);
            return Task.CompletedTask;
        });
        var matchId = Guid.NewGuid();

        scheduler.Schedule(matchId, "stub-alice", 100);
        scheduler.Schedule(matchId, "stub-bob", 100);
        await Task.Delay(400);

        fired.Should().ContainSingle().Which.Should().Be("stub-bob");
    }

    [Fact]
    public async Task Cancel_StopsCallback()
    {
        var fired = false;
        var scheduler = new MatchTimeoutScheduler((id, holder, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });
        var matchId = Guid.NewGuid();

        scheduler.Schedule(matchId, "stub-alice", 100);
        scheduler.Cancel(matchId);
        await Task.Delay(300);

        fired.Should().BeFalse();
    }
}
