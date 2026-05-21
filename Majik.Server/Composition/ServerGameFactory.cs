using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Effects;

namespace Majik.Server.Composition;

/// <summary>
/// Server-side wrapper around <see cref="GameRegistry"/> that delegates
/// game creation and removal. The Match orchestrator now owns the SignalR
/// side via MatchHubPublisher.
/// </summary>
public sealed class ServerGameFactory
{
    private readonly GameRegistry _registry;
    private readonly ICardRepository? _cardRepo;

    public ServerGameFactory(GameRegistry registry, ICardRepository? cardRepo = null)
    {
        _registry = registry;
        _cardRepo = cardRepo;
    }

    public GameFacade Create(
        string aliceName,
        string bobName,
        IReadOnlyList<ICard> aliceDeck,
        IReadOnlyList<ICard> bobDeck,
        string? botSeatArchetype = null,
        Action<bool>? onBotThinking = null)
    {
        var facade = _registry.Create(aliceName, bobName, aliceDeck, bobDeck, _cardRepo);
        if (botSeatArchetype != null)
        {
            // vs-Bot match: Bob seat is the bot, driven by HeuristicStrategy.
            // onBotThinking lets the caller (MatchService) bridge the
            // engine-internal callback to the SignalR hub without making
            // Majik.Bot depend on Majik.Server.
            var botCfg = new Majik.Bot.BotConfig(botSeatArchetype);
            facade.ReplaceBobAgent(new Majik.Bot.BotPlayerAgent(facade.Bob, botCfg, onBotThinking));
        }
        return facade;
    }

    public GameFacade? Get(Guid id) => _registry.Get(id);

    public int Count => _registry.Count;

    public bool Delete(Guid id)
    {
        return _registry.Remove(id);
    }
}
