using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Tests.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class MatchCleanupServiceTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchCleanupServiceTests(TestMongoFixture fx) => _fixture = fx;

    private sealed class FakeClock : IClock { public DateTime UtcNow { get; set; } = DateTime.UtcNow; }

    [Fact]
    public async Task Sweep_AbandonsOldOpenMatches()
    {
        var db = _fixture.NewDatabase();
        var repo = new MatchRepository(db);
        await repo.EnsureIndexesAsync(CancellationToken.None);
        var clock = new FakeClock { UtcNow = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc) };

        var oldMatch = NewMatch(MatchState.Open, createdAt: clock.UtcNow - TimeSpan.FromHours(2));
        var fresh = NewMatch(MatchState.Open, createdAt: clock.UtcNow - TimeSpan.FromMinutes(5));
        await repo.InsertAsync(oldMatch, CancellationToken.None);
        await repo.InsertAsync(fresh, CancellationToken.None);

        var svc = new MatchCleanupService(repo, clock, NullLogger<MatchCleanupService>.Instance);
        await svc.RunSweepAsync(CancellationToken.None);

        (await repo.GetByIdAsync(oldMatch.Id, CancellationToken.None))!.State.Should().Be(MatchState.Abandoned);
        (await repo.GetByIdAsync(fresh.Id, CancellationToken.None))!.State.Should().Be(MatchState.Open);
    }

    [Fact]
    public async Task Sweep_LeavesPlayingAndJoinedAlone()
    {
        var db = _fixture.NewDatabase();
        var repo = new MatchRepository(db);
        await repo.EnsureIndexesAsync(CancellationToken.None);
        var clock = new FakeClock { UtcNow = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc) };

        var playing = NewMatch(MatchState.Playing, createdAt: clock.UtcNow - TimeSpan.FromHours(3));
        await repo.InsertAsync(playing, CancellationToken.None);

        var svc = new MatchCleanupService(repo, clock, NullLogger<MatchCleanupService>.Instance);
        await svc.RunSweepAsync(CancellationToken.None);

        (await repo.GetByIdAsync(playing.Id, CancellationToken.None))!.State.Should().Be(MatchState.Playing);
    }

    private static Match NewMatch(MatchState state, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(), State = state, Visibility = MatchVisibility.Public,
        Format = "constructed", ClockMinutes = 20,
        Creator = new MatchPlayer { Sub = "stub-alice", Handle = "Alice", DeckId = "burn", DeckSnapshot = new List<string>() },
        CreatorMillisRemaining = 1_200_000, OpponentMillisRemaining = 1_200_000,
        CreatedAt = createdAt, UpdatedAt = createdAt,
    };
}
