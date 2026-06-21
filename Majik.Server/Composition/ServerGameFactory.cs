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
    private readonly ServerBotOptions _botOptions;

    public ServerGameFactory(
        GameRegistry registry,
        ICardRepository? cardRepo = null,
        IBotDecisionSink? botDecisionSink = null,
        bool botDecisionLoggingEnabled = false,
        ServerBotOptions? botOptions = null)
    {
        _registry = registry;
        _cardRepo = cardRepo;
        _botDecisionSink = botDecisionSink;
        BotDecisionLoggingEnabled = botDecisionLoggingEnabled;
        _botOptions = botOptions ?? new ServerBotOptions();
        _botOptions.Validate();
    }

    /// <summary>
    /// Mirrors the <c>Bot:DecisionLogging:Enabled</c> config flag. This
    /// gates ONLY the process-wide stdout
    /// <see cref="Majik.Bot.Diagnostics.LoggerBotDecisionSink"/> (a dev
    /// diagnostic, default-off in prod for zero log overhead). It does NOT
    /// gate the per-match
    /// <see cref="Majik.Server.Matches.SignalrBotDecisionSink"/>: that is the
    /// always-on, user-facing diagnostics channel feeding the shipped "bot
    /// decisions" panel, so it is wired whenever a hub is present
    /// (see <see cref="Majik.Server.Matches.MatchService.BuildPerMatchBotDecisionSink"/>).
    /// The SignalR push carries no hidden information and is independent of
    /// stdout logging.
    /// </summary>
    public bool BotDecisionLoggingEnabled { get; }

    public GameFacade Create(
        string aliceName,
        string bobName,
        IReadOnlyList<ICard> aliceDeck,
        IReadOnlyList<ICard> bobDeck,
        string? botSeatArchetype = null,
        Action<bool>? onBotThinking = null,
        IBotDecisionSink? extraDecisionSink = null,
        Func<Majik.Core.Api.BotReplay.BotDecisionRecord, Task>? botDecisionRecorder = null,
        Action<Exception>? onBotRecordingDegraded = null)
    {
        var facade = _registry.Create(aliceName, bobName, aliceDeck, bobDeck, _cardRepo);
        if (botSeatArchetype != null)
        {
            InstallBotAgent(
                facade, botSeatArchetype, onBotThinking, extraDecisionSink,
                botDecisionRecorder, botReplayScript: null, onBotRecordingDegraded);
        }
        return facade;
    }

    /// <summary>
    /// Builds the <see cref="Majik.Bot.BotConfig"/> for a vs-bot seat from this
    /// server's <see cref="ServerBotOptions"/>. The strategy / MCTS knobs come
    /// from configuration (see <see cref="ServerBotOptions"/> for the live-flip
    /// rationale + profiled parameters); the MCTS-only fields are inert under
    /// the heuristic default. <c>OpponentArchetype</c> is deliberately never
    /// set — a human opponent's deck is unknown, and the honest search path is
    /// inference (<see cref="ServerBotOptions.InferOpponentArchetype"/>).
    /// <c>SearchConcurrency</c> is set ONLY under <c>mcts</c> (default 1): live
    /// searches on the 1-vCPU box queue on the process-wide gate instead of
    /// splitting the core; the heuristic strategy never searches, so it stays
    /// null (ungated) there. <c>RolloutDepth</c> is pinned to the engine
    /// default "FullTurnPlus" under <c>mcts</c> (it was never config-overridden
    /// in any deployment): the heuristic never rolls out, so it stays null
    /// there. <c>TreeStateReuse</c> follows the same rule (default false =
    /// today's root-replay loop; <c>Bot__TreeStateReuse=true</c> in prod): the
    /// heuristic never searches, so it stays null there.
    /// <c>RootBlockSearch</c> follows the same rule (default TRUE — root
    /// block search ships on; <c>Bot__RootBlockSearch=false</c> is the kill
    /// switch pinning the legacy <c>BlockCombatEval</c> path): the heuristic
    /// never searches blocks, so it stays null there. <c>MaxWorlds</c> /
    /// <c>PerWorldBudgetMs</c> are pinned null (the engine defaults — kMax 8 /
    /// 400 ms determinized world split — never config-overridden).
    /// Internal so tests can assert the exact installed config without digging
    /// the agent out of a facade.
    /// </summary>
    internal Majik.Bot.BotConfig BuildBotConfig(
        string botSeatArchetype, IBotDecisionSink? decisionSink) =>
        new(botSeatArchetype,
            Strategy: _botOptions.Strategy,
            DecisionSink: decisionSink,
            MaxMctsIterations: _botOptions.MaxMctsIterations,
            MaxMctsBudgetMs: _botOptions.MaxMctsBudgetMs,
            InferOpponentArchetype: _botOptions.InferOpponentArchetype,
            SearchConcurrency: _botOptions.Strategy == "mcts"
                ? _botOptions.SearchConcurrency
                : null,
            // RolloutDepth's old config default — the knob was never set away
            // from "FullTurnPlus" in any deployment, so this is byte-identical.
            RolloutDepth: _botOptions.Strategy == "mcts"
                ? "FullTurnPlus"
                : null,
            TreeStateReuse: _botOptions.Strategy == "mcts"
                ? _botOptions.TreeStateReuse
                : null,
            RootBlockSearch: _botOptions.Strategy == "mcts"
                ? _botOptions.RootBlockSearch
                : null,
            // MaxWorlds / PerWorldBudgetMs were never set in any deployment;
            // null = the engine defaults (kMax 8 / 400 ms), byte-identical.
            MaxWorlds: null,
            PerWorldBudgetMs: null);

    /// <summary>
    /// vs-Bot match: install the Bob-seat <see cref="Majik.Bot.BotPlayerAgent"/>
    /// (strategy selected by <see cref="ServerBotOptions.Strategy"/> — the
    /// deterministic heuristic by default, MCTS search when the deployment
    /// opts in). onBotThinking lets the caller (MatchService)
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
    ///
    /// <para><b>Bot-decision persistence:</b> when
    /// <paramref name="botDecisionRecorder"/> is non-null (the caller's
    /// persistence flag is ON) the live bot is wrapped in a
    /// <see cref="Majik.Core.Api.BotReplay.RecordingPlayerAgent"/> so every
    /// answer is durably appended before it is returned. When
    /// <paramref name="botReplayScript"/> is non-empty (rehydration), a
    /// <see cref="Majik.Core.Api.BotReplay.ScriptedPlayerAgent"/> replays the
    /// recorded answers VERBATIM and falls through to the recording wrapper at
    /// the live edge (continuing the stream at botSeq = script.Count) — agents
    /// cannot be swapped once the game has started, so the handoff is composed
    /// in. With a null recorder the bot is installed bare — byte-identical to
    /// the pre-persistence path.</para>
    /// </summary>
    private void InstallBotAgent(
        GameFacade facade,
        string botSeatArchetype,
        Action<bool>? onBotThinking,
        IBotDecisionSink? extraDecisionSink,
        Func<Majik.Core.Api.BotReplay.BotDecisionRecord, Task>? botDecisionRecorder = null,
        IReadOnlyList<Majik.Core.Api.BotReplay.BotDecisionRecord>? botReplayScript = null,
        Action<Exception>? onBotRecordingDegraded = null)
    {
        var composed = CompositeBotDecisionSink.Compose(_botDecisionSink, extraDecisionSink);
        var effectiveSink = ReferenceEquals(composed, NullBotDecisionSink.Instance) ? null : composed;
        var botCfg = BuildBotConfig(botSeatArchetype, effectiveSink);

        Majik.Core.Players.Agents.IPlayerAgent agent =
            new Majik.Bot.BotPlayerAgent(facade.Bob, botCfg, onBotThinking);

        if (botDecisionRecorder != null)
        {
            // An unsupported answer shape is a logged degrade (the live game
            // continues unrecorded from there), never a live-game crash.
            var degrade = onBotRecordingDegraded ?? (_ => { });
            agent = new Majik.Core.Api.BotReplay.RecordingPlayerAgent(
                agent,
                botDecisionRecorder,
                startSeq: botReplayScript?.Count ?? 0,
                onUnsupported: ex => degrade(ex));
        }

        if (botReplayScript is { Count: > 0 })
        {
            agent = new Majik.Core.Api.BotReplay.ScriptedPlayerAgent(
                botReplayScript, continuation: agent);
        }

        facade.ReplaceBobAgent(agent);
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
    /// so the bot seat is re-installed BEFORE replay starts and its prompts never
    /// consume a logged human command (the bot drives itself in-engine; only human
    /// commands were ever logged). Omitting it on a bot match desyncs the replay.
    /// The replay guarantee is RECORD/REPLAY, not same-seed recompute: the caller
    /// passes the match's recorded <paramref name="botReplayScript"/> and the bot
    /// seat answers every replayed prompt VERBATIM from it via
    /// <see cref="Majik.Core.Api.BotReplay.ScriptedPlayerAgent"/> — so even the
    /// wall-clock-budgeted MCTS strategy (<see cref="ServerBotOptions"/>)
    /// rehydrates identically. Past the live edge the script falls through to a
    /// fresh recording wrapper (botSeq continues at script.Count). A recorded-
    /// stream desync still fails gracefully (rehydrate fails, match lost, never
    /// wedged). onBotThinking is null on rehydrate — there is no live thinking
    /// indicator to drive during a fast-forward.</para>
    /// </summary>
    public GameFacade BuildUnregisteredFacade(
        string aliceName,
        string bobName,
        IReadOnlyList<ICard> aliceDeck,
        IReadOnlyList<ICard> bobDeck,
        string? botSeatArchetype = null,
        IReadOnlyList<Majik.Core.Api.BotReplay.BotDecisionRecord>? botReplayScript = null,
        Func<Majik.Core.Api.BotReplay.BotDecisionRecord, Task>? botDecisionRecorder = null,
        Action<Exception>? onBotRecordingDegraded = null,
        IBotDecisionSink? extraDecisionSink = null)
    {
        var facade = GameFacade.Create(aliceName, bobName, aliceDeck, bobDeck, _cardRepo);
        if (botSeatArchetype != null)
        {
            // Bug fix — forward the per-match SignalR decision sink so a
            // rehydrated bot match re-publishes its decisions on the
            // "bot-decision" channel (the create path passes its per-match sink
            // here too; passing null left the portal panel empty post-rehydrate).
            InstallBotAgent(
                facade, botSeatArchetype, onBotThinking: null, extraDecisionSink,
                botDecisionRecorder, botReplayScript, onBotRecordingDegraded);
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
