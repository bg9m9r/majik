using Majik.Core.CardData;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Majik.Bot.Decks;

/// <summary>
/// Background hosted service that validates every card in every registered
/// bot deck exists with IsImplemented=true. Logs (does not throw) on
/// missing cards — server still starts, individual bot matches fail
/// later with clearer errors.
///
/// Runs as a <see cref="BackgroundService"/> rather than blocking host
/// startup (the previous <see cref="IHostedService"/> implementation
/// crashed the entire api when the cards private_service was a few
/// seconds behind on a same-blueprint redeploy — bot validation hit
/// `majik-cards:10000` before the container was listening, threw
/// <c>HttpRequestException</c> out of <c>StartAsync</c>, and the host
/// SIGSEGV'd before it could serve a single request).
///
/// Each lookup is also wrapped in a bounded retry loop so a transient
/// connect failure on the very first call doesn't poison the whole run.
/// </summary>
public class BotDeckValidator : BackgroundService
{
    private readonly ICardRepository _cards;
    private readonly ILogger<BotDeckValidator> _logger;

    public BotDeckValidator(ICardRepository cards, ILogger<BotDeckValidator> logger)
    {
        _cards = cards;
        _logger = logger;
    }

    /// <summary>How long to wait after host start before running validation.
    /// Tests override to zero to avoid sleeping through the prod delay.</summary>
    protected virtual TimeSpan StartupDelay => TimeSpan.FromSeconds(5);

    /// <summary>Backoff schedule for cards-lookup retries.</summary>
    protected virtual IReadOnlyList<TimeSpan> RetryBackoff { get; } = new[]
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief delay so the rest of the host (Kestrel, route registration)
        // is fully up before we start hammering the cards service. A small
        // constant delay is enough to clear the typical Render
        // same-blueprint deploy window where api and cards redeploy in
        // parallel.
        if (StartupDelay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        var missing = new List<(string archetype, string card)>();
        try
        {
            foreach (var archetype in BotDeckCatalog.Archetypes)
            {
                foreach (var name in BotDeckCatalog.Get(archetype))
                {
                    if (stoppingToken.IsCancellationRequested) return;

                    var entity = await LookupWithRetryAsync(name, stoppingToken).ConfigureAwait(false);
                    if (entity is null || !entity.IsImplemented)
                        missing.Add((archetype, name));
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            // Every retry exhausted — cards service is genuinely down,
            // not just slow to start. Log and exit gracefully; the host
            // stays up so requests that don't touch bot decks still work.
            _logger.LogError(ex,
                "BotDeckValidator: cards service unreachable after retries; skipping validation this run.");
            return;
        }

        if (missing.Count > 0)
        {
            _logger.LogWarning(
                "BotDeckValidator: missing/unimplemented cards. Count={Count} Details={Details}",
                missing.Count,
                string.Join("; ", missing.Select(m => $"[{m.archetype}] {m.card}")));
        }
        else
        {
            _logger.LogInformation(
                "BotDeckValidator: all {Count} archetypes validated.",
                BotDeckCatalog.Archetypes.Count);
        }
    }

    private async Task<CardEntity?> LookupWithRetryAsync(string name, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 0; attempt <= RetryBackoff.Count; attempt++)
        {
            try
            {
                return _cards.GetByName(name);
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt >= RetryBackoff.Count) break;
                _logger.LogWarning(ex,
                    "BotDeckValidator: lookup '{Name}' failed (attempt {Attempt}/{Total}); retrying in {Delay}s.",
                    name, attempt + 1, RetryBackoff.Count + 1, RetryBackoff[attempt].TotalSeconds);
                await Task.Delay(RetryBackoff[attempt], ct).ConfigureAwait(false);
            }
        }
        throw last!;
    }
}
