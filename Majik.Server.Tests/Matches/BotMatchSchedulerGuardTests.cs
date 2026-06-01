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

    // Generous ceiling for the fire-and-forget callbacks (which run with zero
    // configured delay) to settle. We poll well below this and bail the
    // instant the count reaches/exceeds the target, so the happy path is
    // fast; the ceiling only matters under heavy CI contention.
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Poll until <paramref name="counting"/> has created at least
    /// <paramref name="target"/> scopes, or the timeout elapses. Replaces a
    /// fixed Task.Delay — the old sleep flaked when a queued callback hadn't
    /// run yet under CI load. Returns once the count settles; the assertion in
    /// the caller then pins the EXACT expected value (so an over-count still
    /// fails).
    /// </summary>
    private static async Task WaitForScopesAtLeastAsync(CountingScopeProvider counting, int target)
    {
        var deadline = DateTime.UtcNow + SettleTimeout;
        while (counting.ScopesCreated < target && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }
    }

    /// <summary>
    /// Give any spurious EXTRA callbacks a brief, bounded window to surface
    /// after the expected count is reached — guards the "exactly N" assertions
    /// (e.g. dedup must NOT create a second scope) against a false pass caused
    /// by checking before a stray callback ran. Short and fixed because we are
    /// proving the ABSENCE of further work, which can't be polled-for.
    /// </summary>
    private static Task DrainStrayCallbacksAsync() => Task.Delay(150);

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

        // Wait for the one allowed callback to run, then give any (wrongly)
        // queued second callback a bounded window to surface.
        await WaitForScopesAtLeastAsync(counting, 1);
        await DrainStrayCallbacksAsync();

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

        await WaitForScopesAtLeastAsync(counting, 2);
        await DrainStrayCallbacksAsync();

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

        // First schedule — callback fires (scope #1), key is evicted in the
        // callback's finally AFTER PlayDrawAsync completes.
        scheduler.ScheduleBotPlayDraw(matchId, "bot:aggro");
        await WaitForScopesAtLeastAsync(counting, 1);

        // Second schedule. The dedup key is evicted only once the first
        // callback reaches its finally — which is NOT guaranteed merely
        // because scope #1 was created (eviction trails the scope). The old
        // fixed 200 ms sleep raced that window. Re-issue the schedule (a no-op
        // while the key is still present — exactly the production behaviour)
        // until the eviction lands and a second scope is created.
        var rescheduleDeadline = DateTime.UtcNow + SettleTimeout;
        while (counting.ScopesCreated < 2 && DateTime.UtcNow < rescheduleDeadline)
        {
            scheduler.ScheduleBotPlayDraw(matchId, "bot:aggro");
            await Task.Delay(10);
        }
        await DrainStrayCallbacksAsync();

        counting.ScopesCreated.Should().Be(2,
            "the dedup entry is evicted after the callback completes, so a re-schedule is allowed");
    }
}
