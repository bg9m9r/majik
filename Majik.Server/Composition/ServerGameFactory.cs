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
            InstallBotAgent(facade, botSeatArchetype, onBotThinking, extraDecisionSink);
        }
        return facade;
    }

    /// <summary>
    /// vs-Bot match: install the Bob-seat <see cref="Majik.Bot.BotPlayerAgent"/>
    /// (driven by HeuristicStrategy). onBotThinking lets the caller (MatchService)
    /// bridge the engine-internal callback to the SignalR hub without making
    /// Majik.Bot depend on Majik.Server. Shared between fresh match creation
    /// (<see cref="Create"/>) and rehydration (<see cref="BuildUnregisteredFacade"/>)
    /// so a rehydrated bot match comes back with the SAME deterministic agent —
    /// without it the bot seat would be left on the default RemoteAgent and the
    /// prompt-driven replay would dequeue human commands against bot prompts
    /// (desync).
    ///
    /// Sink composition rules (see CompositeBotDecisionSink.Compose):
    ///   * Both null → BotConfig.DecisionSink stays null → engine uses
    ///     NullBotDecisionSink (zero overhead).
    ///   * Exactly one non-null → that sink is used directly.
    ///   * Both non-null → wrapped in a CompositeBotDecisionSink so each
    ///     decision fans out to logger + SignalR (or whatever extra sink the
    ///     caller passed).
    /// extraDecisionSink is per-match (e.g. SignalR keyed on matchId);
    /// _botDecisionSink is the process-wide logger sink. The sink is diagnostics
    /// only — it does not affect the bot's deterministic decisions, so a
    /// rehydrate can safely pass null and still reproduce the original play.
    /// </summary>
    private void InstallBotAgent(
        GameFacade facade,
        string botSeatArchetype,
        Action<bool>? onBotThinking,
        IBotDecisionSink? extraDecisionSink)
    {
        var composed = CompositeBotDecisionSink.Compose(_botDecisionSink, extraDecisionSink);
        var effectiveSink = ReferenceEquals(composed, NullBotDecisionSink.Instance) ? null : composed;
        var botCfg = new Majik.Bot.BotConfig(botSeatArchetype, DecisionSink: effectiveSink);
        facade.ReplaceBobAgent(new Majik.Bot.BotPlayerAgent(facade.Bob, botCfg, onBotThinking));
    }

    /// <summary>
    /// PLAN 08 (body) — build a facade WITHOUT registering it (no GameRegistry
    /// entry). Used as the <c>buildFreshFacade</c> step inside
    /// <see cref="GameFacade.Rehydrate"/>: the rehydration replays the durable log
    /// onto this fresh facade under a seed-scope, and only the FINISHED live
    /// facade is registered (via <see cref="RegisterRehydrated"/>) under the
    /// original match game id. Building through the registry here would mint +
    /// register a throwaway GameId and collide on rebuild. The same
    /// <see cref="ICardRepository"/> the live game used is threaded in so the
    /// rebuilt deck cards bind identically.
    ///
    /// <para>For a vs-bot match the caller passes <paramref name="botSeatArchetype"/>
    /// so the deterministic <see cref="Majik.Bot.BotPlayerAgent"/> is re-installed
    /// on the Bob seat BEFORE replay starts — same archetype + same seed ⇒ the
    /// bot re-makes identical in-engine decisions, and its prompts never consume
    /// a logged human command (the bot drives itself via ChooseAsync; only human
    /// commands were ever logged). Omitting it on a bot match desyncs the replay.
    /// onBotThinking is null on rehydrate — there is no live thinking indicator to
    /// drive during a fast-forward.</para>
    /// </summary>
    public GameFacade BuildUnregisteredFacade(
        string aliceName,
        string bobName,
        IReadOnlyList<ICard> aliceDeck,
        IReadOnlyList<ICard> bobDeck,
        string? botSeatArchetype = null)
    {
        var facade = GameFacade.Create(aliceName, bobName, aliceDeck, bobDeck, _cardRepo);
        if (botSeatArchetype != null)
        {
            InstallBotAgent(facade, botSeatArchetype, onBotThinking: null, extraDecisionSink: null);
        }
        return facade;
    }

    /// <summary>
    /// PLAN 08 (body) — register an already-rehydrated facade under the original
    /// match game id. The caller (MatchService, under a won ownership claim) has
    /// re-stamped <paramref name="facade"/>'s GameId to <paramref name="gameId"/>.
    /// Returns false when the id was concurrently registered (the live facade
    /// wins — no clobber).
    /// </summary>
    public bool RegisterRehydrated(Guid gameId, GameFacade facade)
        => _registry.RegisterRehydrated(gameId, facade);

    public GameFacade? Get(Guid id) => _registry.Get(id);

    public int Count => _registry.Count;

    public bool Delete(Guid id)
    {
        return _registry.Remove(id);
    }
}
