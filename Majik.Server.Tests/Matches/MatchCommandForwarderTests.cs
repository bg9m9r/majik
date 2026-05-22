using FluentAssertions;
using Majik.Core.Api.Commands;
using Majik.Server.Composition;
using Majik.Server.Matches;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class MatchCommandForwarderTests
{
    private static MatchCommandForwarder Build() => new MatchCommandForwarder(
        new InstanceIdProvider("test-instance"),
        new ServiceCollection().BuildServiceProvider(),
        NullLogger<MatchCommandForwarder>.Instance,
        redis: null);

    [Fact]
    public async Task SendAsync_NoRedis_ReturnsFalse()
    {
        var forwarder = Build();
        var ok = await forwarder.SendAsync(
            Guid.NewGuid(),
            "sub-x",
            new PassPriorityCommand(),
            CancellationToken.None);
        ok.Should().BeFalse("with no Redis configured, forwarding is impossible");
    }

    [Fact]
    public async Task OnClaimed_NoRedis_IsNoOp()
    {
        var forwarder = Build();
        await forwarder.OnClaimedAsync(Guid.NewGuid(), CancellationToken.None);
        // Just asserting no throw — without Redis the call is inert.
    }

    [Fact]
    public async Task OnReleased_NoRedis_IsIdempotent()
    {
        var forwarder = Build();
        var matchId = Guid.NewGuid();
        await forwarder.OnReleasedAsync(matchId, CancellationToken.None);
        await forwarder.OnReleasedAsync(matchId, CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_NoRedis_DoesNotThrow()
    {
        var forwarder = Build();
        await forwarder.StartAsync(CancellationToken.None);
        await forwarder.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void ReplyTimeout_LongerThanTypicalCommandLatency()
    {
        // Sanity bound — too short and slow Redis hops drop legitimate commands.
        MatchCommandForwarder.ReplyTimeout.Should().BeGreaterThan(TimeSpan.FromSeconds(1));
    }
}
