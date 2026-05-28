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

        // Without Redis the claim notify is inert. Contract: must not throw.
        var act = async () => await forwarder.OnClaimedAsync(Guid.NewGuid(), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnReleased_NoRedis_IsIdempotent()
    {
        var forwarder = Build();
        var matchId = Guid.NewGuid();

        // Double-release for the same match id must be a clean no-op without
        // Redis — contract: idempotent and non-throwing.
        var act = async () =>
        {
            await forwarder.OnReleasedAsync(matchId, CancellationToken.None);
            await forwarder.OnReleasedAsync(matchId, CancellationToken.None);
        };
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_NoRedis_DoesNotThrow()
    {
        var forwarder = Build();

        // Lifecycle (start + stop) must be inert without Redis. Contract:
        // must not throw — the forwarder simply has nothing to subscribe to.
        var act = async () =>
        {
            await forwarder.StartAsync(CancellationToken.None);
            await forwarder.StopAsync(CancellationToken.None);
        };
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void ReplyTimeout_LongerThanTypicalCommandLatency()
    {
        // Sanity bound — too short and slow Redis hops drop legitimate commands.
        MatchCommandForwarder.ReplyTimeout.Should().BeGreaterThan(TimeSpan.FromSeconds(1));
    }
}
