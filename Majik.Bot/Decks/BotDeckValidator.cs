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
/// startup so a slow validation pass never delays Kestrel coming up.
///
/// Card data is embedded in the <c>Majik.Core</c> assembly and loaded
/// in-process by <c>EmbeddedCardRepository</c> — there is no network hop,
/// so <see cref="ICardRepository.GetByName"/> is a plain dictionary lookup
/// that can't raise a transient connect error. The old startup delay /
/// retry-backoff scaffolding existed for a now-removed <c>majik-cards</c>
/// HTTP service and has been dropped.
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

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var missing = new List<(string archetype, string card)>();
        foreach (var archetype in BotDeckCatalog.Archetypes)
        {
            foreach (var name in BotDeckCatalog.Get(archetype))
            {
                if (stoppingToken.IsCancellationRequested) return Task.CompletedTask;

                var entity = _cards.GetByName(name);
                if (entity is null || !entity.IsImplemented)
                    missing.Add((archetype, name));
            }
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

        return Task.CompletedTask;
    }
}
