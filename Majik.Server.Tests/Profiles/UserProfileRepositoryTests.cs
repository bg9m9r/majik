using FluentAssertions;
using Majik.Server.Profiles;
using MongoDB.Driver;
using Xunit;

namespace Majik.Server.Tests.Profiles;

public class UserProfileRepositoryTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;

    public UserProfileRepositoryTests(TestMongoFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<UserProfileRepository> NewRepoAsync()
    {
        var db = _fixture.NewDatabase();
        var repo = new UserProfileRepository(db);
        await repo.EnsureIndexesAsync(CancellationToken.None);
        return repo;
    }

    [Fact]
    public async Task UpsertThenGet_RoundTrips()
    {
        var repo = await NewRepoAsync();
        var profile = new UserProfile
        {
            Sub = "stub-alice",
            Handle = "alice",
            HandleDisplay = "Alice",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await repo.UpsertAsync(profile, CancellationToken.None);
        var fetched = await repo.GetBySubAsync("stub-alice", CancellationToken.None);

        fetched.Should().NotBeNull();
        fetched!.HandleDisplay.Should().Be("Alice");
        fetched.Handle.Should().Be("alice");
    }

    [Fact]
    public async Task UpsertSameSub_UpdatesFields()
    {
        var repo = await NewRepoAsync();
        var original = new UserProfile
        {
            Sub = "stub-alice", Handle = "alice", HandleDisplay = "Alice",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        await repo.UpsertAsync(original, CancellationToken.None);

        var updated = new UserProfile
        {
            Sub = "stub-alice", Handle = "aliceb", HandleDisplay = "AliceB",
            CreatedAt = original.CreatedAt, UpdatedAt = DateTime.UtcNow,
        };
        await repo.UpsertAsync(updated, CancellationToken.None);

        var fetched = await repo.GetBySubAsync("stub-alice", CancellationToken.None);
        fetched!.Handle.Should().Be("aliceb");
        fetched.HandleDisplay.Should().Be("AliceB");
        fetched.CreatedAt.Should().BeCloseTo(original.CreatedAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task DuplicateHandle_ThrowsMongoWriteException()
    {
        var repo = await NewRepoAsync();
        await repo.UpsertAsync(new UserProfile
        {
            Sub = "stub-alice", Handle = "alice", HandleDisplay = "Alice",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        }, CancellationToken.None);

        var act = () => repo.UpsertAsync(new UserProfile
        {
            Sub = "stub-bob", Handle = "alice", HandleDisplay = "alice",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        }, CancellationToken.None);

        await act.Should().ThrowAsync<MongoWriteException>();
    }

    [Fact]
    public async Task IsHandleTaken_TrueWhenOtherSubHas()
    {
        var repo = await NewRepoAsync();
        await repo.UpsertAsync(new UserProfile
        {
            Sub = "stub-alice", Handle = "alice", HandleDisplay = "Alice",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        }, CancellationToken.None);

        var taken = await repo.IsHandleTakenAsync("alice", excludeSub: "stub-bob", CancellationToken.None);
        taken.Should().BeTrue();
    }

    [Fact]
    public async Task IsHandleTaken_FalseWhenOnlySelfHas()
    {
        var repo = await NewRepoAsync();
        await repo.UpsertAsync(new UserProfile
        {
            Sub = "stub-alice", Handle = "alice", HandleDisplay = "Alice",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        }, CancellationToken.None);

        var taken = await repo.IsHandleTakenAsync("alice", excludeSub: "stub-alice", CancellationToken.None);
        taken.Should().BeFalse();
    }
}
