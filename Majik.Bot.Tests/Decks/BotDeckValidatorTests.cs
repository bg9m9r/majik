using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Majik.Bot.Tests.Decks;

/// <summary>
/// Locks the resilience contract on BotDeckValidator: when the upstream
/// card lookup throws (e.g. cards private_service unreachable during a
/// same-blueprint cold start) ExecuteAsync must NOT propagate the
/// exception out of the BackgroundService — otherwise it crashes the
/// whole api host (which is exactly what was happening pre-fix).
/// </summary>
public class BotDeckValidatorTests
{
    [Fact]
    public async Task ExecuteAsync_LookupAlwaysThrows_Swallows_DoesNotCrashHost()
    {
        var repo = new AlwaysThrowsRepo();
        var sut = new InstantValidator(repo);

        // Should complete normally even though every lookup throws.
        await sut.RunOnce(CancellationToken.None);

        repo.CallCount.Should().BeGreaterThan(0,
            "validator should have attempted at least one lookup");
    }

    [Fact]
    public async Task ExecuteAsync_LookupSucceedsAfterFlap_RecoversWithoutThrowing()
    {
        var repo = new FlappyRepo(failFirst: 2);
        var sut = new InstantValidator(repo);

        await sut.RunOnce(CancellationToken.None);

        repo.CallCount.Should().BeGreaterThanOrEqualTo(3,
            "first 2 throws + recovery on 3rd call");
    }

    /// <summary>Zeroes out the startup delay and the inter-retry backoff
    /// so tests don't sit through prod timeouts.</summary>
    private sealed class InstantValidator : BotDeckValidator
    {
        public InstantValidator(ICardRepository cards)
            : base(cards, NullLogger<BotDeckValidator>.Instance) { }

        protected override TimeSpan StartupDelay => TimeSpan.Zero;
        protected override IReadOnlyList<TimeSpan> RetryBackoff { get; } = new[]
        {
            TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
        };

        public Task RunOnce(CancellationToken ct)
        {
            // ExecuteAsync is protected; invoke via the BackgroundService
            // base contract — StartAsync internally calls ExecuteAsync and
            // returns immediately (without awaiting). To deterministically
            // observe completion, drive ExecuteAsync via reflection.
            var mi = typeof(BotDeckValidator).GetMethod(
                "ExecuteAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            return (Task)mi.Invoke(this, new object[] { ct })!;
        }
    }

    private sealed class AlwaysThrowsRepo : ICardRepository
    {
        public int CallCount;

        public CardEntity? GetByName(string name)
        {
            Interlocked.Increment(ref CallCount);
            throw new HttpRequestException("simulated cards-service-down");
        }

        public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit,
            IReadOnlyList<string>? colors = null, IReadOnlyList<string>? types = null,
            IReadOnlyList<int>? cmcBuckets = null) => Array.Empty<CardEntity>();
        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) => Array.Empty<CardEntity>();
        public bool IsImplemented(string name) => false;
        public void SetImplemented(string name, bool value) { }
        public BotIntent IntentFor(string cardName) => BotIntent.None;
    }

    private sealed class FlappyRepo : ICardRepository
    {
        private readonly int _failFirst;
        public int CallCount;

        public FlappyRepo(int failFirst) => _failFirst = failFirst;

        public CardEntity? GetByName(string name)
        {
            var call = Interlocked.Increment(ref CallCount);
            if (call <= _failFirst)
                throw new HttpRequestException("simulated transient cards flap");
            return new CardEntity { Name = name, IsImplemented = true };
        }

        public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit,
            IReadOnlyList<string>? colors = null, IReadOnlyList<string>? types = null,
            IReadOnlyList<int>? cmcBuckets = null) => Array.Empty<CardEntity>();
        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) => Array.Empty<CardEntity>();
        public bool IsImplemented(string name) => false;
        public void SetImplemented(string name, bool value) { }
        public BotIntent IntentFor(string cardName) => BotIntent.None;
    }
}
