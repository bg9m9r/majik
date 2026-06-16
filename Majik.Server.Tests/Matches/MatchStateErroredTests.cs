using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Helpers;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Task W1 — the engine-wedge-supervision fix needs a distinct terminal
/// <see cref="MatchState.Errored"/> (separate from <see cref="MatchState.Completed"/>,
/// which carries winner semantics). These tests pin (a) the enum member exists and
/// serializes to "Errored", and (b) an already-Errored match is treated as terminal
/// by <see cref="MatchService.AbandonAsync"/> — same rejection a Completed match gets,
/// with no mutation.
/// </summary>
public class MatchStateErroredTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchStateErroredTests(TestMongoFixture fixture) => _fixture = fixture;

    private sealed class StubRandomSource : IRandomSource
    {
        private readonly Queue<int> _values;
        public StubRandomSource(params int[] values) => _values = new Queue<int>(values);
        public int NextInt(int min, int max) => _values.Dequeue();
    }

    [Fact]
    public void Errored_IsDefinedEnumMember_SerializingToErrored()
    {
        MatchState.Errored.ToString().Should().Be("Errored");
        Enum.IsDefined(typeof(MatchState), MatchState.Errored).Should().BeTrue();
    }

    [Fact]
    public async Task AbandonAsync_WhenStateErrored_RejectsTerminal_NoMutation()
    {
        var db = _fixture.NewDatabase();
        var matchRepo = new MatchRepository(db);
        await matchRepo.EnsureIndexesAsync(CancellationToken.None);
        var profileRepo = new UserProfileRepository(db);
        await profileRepo.EnsureIndexesAsync(CancellationToken.None);

        var now = DateTime.UtcNow;
        await profileRepo.UpsertAsync(new UserProfile { Sub = "stub-alice", Handle = "alice", HandleDisplay = "Alice", CreatedAt = now, UpdatedAt = now }, CancellationToken.None);
        await profileRepo.UpsertAsync(new UserProfile { Sub = "stub-bob", Handle = "bob", HandleDisplay = "Bob", CreatedAt = now, UpdatedAt = now }, CancellationToken.None);

        // Insert a match already in the terminal Errored state.
        var match = new Match
        {
            Id = Guid.NewGuid(),
            State = MatchState.Errored,
            Visibility = MatchVisibility.Public,
            Format = "constructed",
            ClockMinutes = 20,
            Creator = new MatchPlayer { Sub = "stub-alice", Handle = "Alice", DeckId = "burn", DeckSnapshot = new List<string>() },
            Opponent = new MatchPlayer { Sub = "stub-bob", Handle = "Bob", DeckId = "stompy", DeckSnapshot = new List<string>() },
            CreatorMillisRemaining = 1_200_000L,
            OpponentMillisRemaining = 1_200_000L,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await matchRepo.InsertAsync(match, CancellationToken.None);

        var svc = new MatchService(matchRepo, profileRepo,
            new DiceRoller(new StubRandomSource()), new StubDeckLoader(), new SystemClock(),
            null, timeoutScheduler: null, gameFactory: null,
            deckOwnershipPolicy: new AllowStubDeckOwnershipPolicy());

        var result = await svc.AbandonAsync("stub-alice", match.Id, CancellationToken.None);

        // Same terminal rejection a Completed match gets (AbandonAsync guard).
        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("match-in-progress");

        // No mutation: the match is still Errored.
        var fresh = await matchRepo.GetByIdAsync(match.Id, CancellationToken.None);
        fresh!.State.Should().Be(MatchState.Errored);
    }
}
