using Majik.Core.CardData;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Majik.Bot.Decks;

/// <summary>
/// Startup hosted service that validates every card in every registered
/// bot deck exists with IsImplemented=true. Logs (does not throw) on
/// missing cards — server still starts, individual bot matches fail
/// later with clearer errors.
/// </summary>
public sealed class BotDeckValidator : IHostedService
{
    private readonly ICardRepository _cards;
    private readonly ILogger<BotDeckValidator> _logger;

    public BotDeckValidator(ICardRepository cards, ILogger<BotDeckValidator> logger)
    {
        _cards = cards;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var missing = new List<(string archetype, string card)>();
        foreach (var archetype in BotDeckCatalog.Archetypes)
        {
            foreach (var name in BotDeckCatalog.Get(archetype))
            {
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
            _logger.LogInformation("BotDeckValidator: all {Count} archetypes validated.", BotDeckCatalog.Archetypes.Count);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
