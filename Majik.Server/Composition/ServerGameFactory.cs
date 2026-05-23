using Majik.Bot.Diagnostics;
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
    private readonly IBotDecisionSink? _botDecisionSink;

    public ServerGameFactory(
        GameRegistry registry,
        ICardRepository? cardRepo = null,
        IBotDecisionSink? botDecisionSink = null,
        bool botDecisionLoggingEnabled = false)
    {
        _registry = registry;
        _cardRepo = cardRepo;
        _botDecisionSink = botDecisionSink;
        BotDecisionLoggingEnabled = botDecisionLoggingEnabled;
    }

    /// <summary>
    /// Mirrors the <c>Bot:DecisionLogging:Enabled</c> config flag. Callers
    /// that want to compose a per-match decision sink (e.g.
    /// <see cref="Majik.Server.Matches.SignalrBotDecisionSink"/> keyed on
    /// matchId) gate on this so the SignalR fan-out toggles in lockstep
    /// with the singleton logger sink — no half-on state where the wire
    /// channel is alive but server stdout is quiet (or vice versa).
    /// </summary>
    public bool BotDecisionLoggingEnabled { get; }

    public GameFacade Create(
        string aliceName,
        string bobName,
        IReadOnlyList<ICard> aliceDeck,
        IReadOnlyList<ICard> bobDeck,
        string? botSeatArchetype = null,
        Action<bool>? onBotThinking = null,
        IBotDecisionSink? extraDecisionSink = null)
    {
        var facade = _registry.Create(aliceName, bobName, aliceDeck, bobDeck, _cardRepo);
        if (botSeatArchetype != null)
        {
            // vs-Bot match: Bob seat is the bot, driven by HeuristicStrategy.
            // onBotThinking lets the caller (MatchService) bridge the
            // engine-internal callback to the SignalR hub without making
            // Majik.Bot depend on Majik.Server.
            //
            // Sink composition rules (see CompositeBotDecisionSink.Compose):
            //   * Both null → BotConfig.DecisionSink stays null → engine
            //     uses NullBotDecisionSink (zero overhead).
            //   * Exactly one non-null → that sink is used directly.
            //   * Both non-null → wrapped in a CompositeBotDecisionSink so
            //     each decision fans out to logger + SignalR (or whatever
            //     extra sink the caller passed).
            // extraDecisionSink is per-match (e.g. SignalR keyed on
            // matchId); _botDecisionSink is the process-wide logger sink.
            var composed = CompositeBotDecisionSink.Compose(_botDecisionSink, extraDecisionSink);
            var effectiveSink = ReferenceEquals(composed, NullBotDecisionSink.Instance) ? null : composed;
            var botCfg = new Majik.Bot.BotConfig(botSeatArchetype, DecisionSink: effectiveSink);
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
