using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Helpers;
using Majik.Server.Tests.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Slice 4a #7 — the production <see cref="BotMatchScheduler"/> must schedule
/// the bot's play/draw follow-up at most ONCE per match, even under a rapid
/// double-fire (two rolls landing nearly simultaneously). The guard runs
/// before the fire-and-forget callback is queued, so a second call for the
/// same matchId must not resolve a second <see cref="MatchService"/> scope.
/// </summary>
public class BotMatchSchedulerGuardTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public BotMatchSchedulerGuardTests(TestMongoFixture fixture) => _fixture = fixture;

    /// <summary>Wraps a real provider, counting how many DI scopes are created
    /// — one per callback that gets past the dedup guard.</summary>
    private sealed class CountingScopeProvider : IServiceProvider, IServiceScopeFactory
    {
        private readonly IServiceProvider _inner;
        private int _scopesCreated;
        public int ScopesCreated => Volatile.Read(ref _scopesCreated);

        public CountingScopeProvider(IServiceProvider inner) => _inner = inner;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IServiceScopeFactory) ? this : _inner.GetService(serviceType);

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref _scopesCreated);
            return _inner.CreateScope();
        }
    }

    private ServiceProvider BuildContainer()
    {
        var db = _fixture.NewDatabase();
        var matchRepo = new MatchRepository(db);
        matchRepo.EnsureIndexesAsync(CancellationToken.None).GetAwaiter().GetResult();
        var profileRepo = new UserProfileRepository(db);
        profileRepo.EnsureIndexesAsync(CancellationToken.None).GetAwaiter().GetResult();

        var services = new ServiceCollection();
        services.AddSingleton(matchRepo);
        services.AddSingleton(profileRepo);
        services.AddSingleton<IRandomSource>(new SystemRandomSource());
        services.AddSingleton<DiceRoller>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped(sp => new MatchService(
            sp.GetRequiredService<MatchRepository>(),
            sp.GetRequiredService<UserProfileRepository>(),
            sp.GetRequiredService<DiceRoller>(),
            new StubDeckLoader(),
            sp.GetRequiredService<IClock>(),
            hub: null,
            timeoutScheduler: new MatchTimeoutScheduler((_, _, _) => Task.CompletedTask),
            gameFactory: null,
            deckOwnershipPolicy: new AllowStubDeckOwnershipPolicy()));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ScheduleBotPlayDraw_DoubleFire_ResolvesAtMostOneScope()
    {
        using var inner = BuildContainer();
        var counting = new CountingScopeProvider(inner);

        // Zero delay so the callback runs promptly; matchId is random so
        // PlayDrawAsync returns match-not-found harmlessly (no throw).
        var scheduler = new BotMatchScheduler(
            counting, logger: null,
            rollDelay: TimeSpan.Zero, playDrawDelay: TimeSpan.Zero);

        var matchId = Guid.NewGuid();
        // Rapid double-fire for the same match.
        scheduler.ScheduleBotPlayDraw(matchId, "bot:aggro");
        scheduler.ScheduleBotPlayDraw(matchId, "bot:aggro");

        // Let any queued callbacks run.
        await Task.Delay(200);

        counting.ScopesCreated.Should().Be(1,
            "the per-match guard must let only the first ScheduleBotPlayDraw queue a callback");
    }

    [Fact]
    public async Task ScheduleBotPlayDraw_DifferentMatches_EachScheduledOnce()
    {
        using var inner = BuildContainer();
        var counting = new CountingScopeProvider(inner);
        var scheduler = new BotMatchScheduler(
            counting, logger: null,
            rollDelay: TimeSpan.Zero, playDrawDelay: TimeSpan.Zero);

        scheduler.ScheduleBotPlayDraw(Guid.NewGuid(), "bot:aggro");
        scheduler.ScheduleBotPlayDraw(Guid.NewGuid(), "bot:aggro");

        await Task.Delay(200);

        counting.ScopesCreated.Should().Be(2, "distinct matches are independent — each schedules once");
    }

    /// <summary>
    /// After the play/draw callback has run (and cleared the dedup entry in
    /// <c>finally</c>), a second call for the same matchId must be able to
    /// schedule again — the dict must not retain the key after the callback
    /// completes (eviction correctness).
    /// </summary>
    [Fact]
    public async Task ScheduleBotPlayDraw_AfterCallbackCompletes_AllowsReschedule()
    {
        using var inner = BuildContainer();
        var counting = new CountingScopeProvider(inner);
        var scheduler = new BotMatchScheduler(
            counting, logger: null,
            rollDelay: TimeSpan.Zero, playDrawDelay: TimeSpan.Zero);

        var matchId = Guid.NewGuid();

        // First schedule — callback fires, key is evicted in finally.
        scheduler.ScheduleBotPlayDraw(matchId, "bot:aggro");
        await Task.Delay(200); // wait for callback to complete

        // Second schedule — the dedup key is gone so a second scope is created.
        scheduler.ScheduleBotPlayDraw(matchId, "bot:aggro");
        await Task.Delay(200);

        counting.ScopesCreated.Should().Be(2,
            "the dedup entry is evicted after the callback completes, so a re-schedule is allowed");
    }
}
