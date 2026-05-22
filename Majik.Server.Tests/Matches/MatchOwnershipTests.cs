using FluentAssertions;
using Majik.Server.Composition;
using Majik.Server.Matches;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Unit tests against the no-Redis fallback branch. The Redis-backed
/// branch is integration-tested at deploy time — there's no embedded
/// Redis in the test rig and the API surface (StringSetAsync/KeyExpire/
/// StringGet/KeyDelete) is thin enough that a Redis-mocked test would
/// just assert "I called the methods I called".
/// </summary>
public class MatchOwnershipTests
{
    private static MatchOwnership Build(string instanceId = "instance-a") =>
        new MatchOwnership(new InstanceIdProvider(instanceId), redis: null);

    [Fact]
    public async Task TryClaim_NoRedis_AlwaysSucceeds_AndRecordsLocally()
    {
        var ownership = Build();
        var matchId = Guid.NewGuid();

        var claimed = await ownership.TryClaimAsync(matchId, CancellationToken.None);

        claimed.Should().BeTrue();
        ownership.Owned.Should().Contain(matchId);
    }

    [Fact]
    public async Task GetOwner_NoRedis_ReturnsInstanceForClaimedMatch()
    {
        var ownership = Build("instance-foo");
        var matchId = Guid.NewGuid();
        await ownership.TryClaimAsync(matchId, CancellationToken.None);

        var owner = await ownership.GetOwnerAsync(matchId, CancellationToken.None);

        owner.Should().Be("instance-foo");
    }

    [Fact]
    public async Task GetOwner_NoRedis_ReturnsNullForUnclaimedMatch()
    {
        var ownership = Build();
        var owner = await ownership.GetOwnerAsync(Guid.NewGuid(), CancellationToken.None);
        owner.Should().BeNull();
    }

    [Fact]
    public async Task Refresh_NoRedis_TrueIfClaimed_FalseOtherwise()
    {
        var ownership = Build();
        var claimed = Guid.NewGuid();
        var notClaimed = Guid.NewGuid();
        await ownership.TryClaimAsync(claimed, CancellationToken.None);

        (await ownership.RefreshAsync(claimed, CancellationToken.None)).Should().BeTrue();
        (await ownership.RefreshAsync(notClaimed, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Release_NoRedis_RemovesFromOwned()
    {
        var ownership = Build();
        var matchId = Guid.NewGuid();
        await ownership.TryClaimAsync(matchId, CancellationToken.None);

        await ownership.ReleaseAsync(matchId, CancellationToken.None);

        ownership.Owned.Should().NotContain(matchId);
        (await ownership.GetOwnerAsync(matchId, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Release_NoRedis_IsIdempotent()
    {
        var ownership = Build();
        var matchId = Guid.NewGuid();
        await ownership.ReleaseAsync(matchId, CancellationToken.None); // never claimed
        ownership.Owned.Should().BeEmpty();
    }

    [Fact]
    public void OwnerTtl_LongerThanHeartbeatInterval()
    {
        // Sanity: heartbeat must fire well below TTL or owners lose claims on a good day.
        MatchOwnership.OwnerTtl.Should().BeGreaterThan(MatchOwnershipHeartbeat.Interval * 2);
    }
}
