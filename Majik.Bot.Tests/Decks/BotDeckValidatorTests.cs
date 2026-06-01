using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Majik.Bot.Tests.Decks;

/// <summary>
/// Locks the BotDeckValidator contract: it runs as a background service that
/// looks up every card in every catalog deck via the in-process
/// <see cref="ICardRepository"/> and LOGS (never throws) when a card is
/// missing or unimplemented. The old network-hop scaffolding (startup delay,
/// retry/backoff, "cards service unreachable" handling) was removed when the
/// separate majik-cards HTTP service went away — card data is now embedded in
/// Majik.Core and loaded in-process, so GetByName is a dictionary lookup that
/// can't raise a transient connect error.
/// </summary>
public class BotDeckValidatorTests
{
    [Fact]
    public async Task ExecuteAsync_AllCardsImplemented_LogsValidatedNoWarnings()
    {
        var repo = new StubRepo(implementedAll: true);
        var logger = new CapturingLogger();
        var sut = new TestValidator(repo, logger);

        await sut.RunOnce(CancellationToken.None);

        repo.CallCount.Should().BeGreaterThan(0,
            "validator should have looked up every catalog card");
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning,
            "no card is missing, so no warning should be logged");
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information && e.Message.Contains("validated"));
    }

    [Fact]
    public async Task ExecuteAsync_SomeCardsMissing_LogsWarningWithDetails()
    {
        // Repo reports every card as unimplemented → all are "missing".
        var repo = new StubRepo(implementedAll: false);
        var logger = new CapturingLogger();
        var sut = new TestValidator(repo, logger);

        await sut.RunOnce(CancellationToken.None);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("missing/unimplemented"),
            "unimplemented cards must surface as a warning, not crash the host");
    }

    /// <summary>Drives the protected ExecuteAsync directly so the test can
    /// observe completion deterministically (the BackgroundService base
    /// StartAsync returns before ExecuteAsync finishes).</summary>
    private sealed class TestValidator : BotDeckValidator
    {
        public TestValidator(ICardRepository cards, ILogger<BotDeckValidator> logger)
            : base(cards, logger) { }

        public Task RunOnce(CancellationToken ct)
        {
            var mi = typeof(BotDeckValidator).GetMethod(
                "ExecuteAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            return (Task)mi.Invoke(this, new object[] { ct })!;
        }
    }

    private sealed class StubRepo : ICardRepository
    {
        private readonly bool _implementedAll;
        public int CallCount;

        public StubRepo(bool implementedAll) => _implementedAll = implementedAll;

        public CardEntity? GetByName(string name)
        {
            Interlocked.Increment(ref CallCount);
            return new CardEntity { Name = name, IsImplemented = _implementedAll };
        }

        public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit,
            IReadOnlyList<string>? colors = null, IReadOnlyList<string>? types = null,
            IReadOnlyList<int>? cmcBuckets = null) => Array.Empty<CardEntity>();
        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) => Array.Empty<CardEntity>();
        public bool IsImplemented(string name) => _implementedAll;
        public void SetImplemented(string name, bool value) { }
        public BotIntent IntentFor(string cardName) => BotIntent.None;
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger : ILogger<BotDeckValidator>
    {
        public readonly List<LogEntry> Entries = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NullLogger.Instance.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
