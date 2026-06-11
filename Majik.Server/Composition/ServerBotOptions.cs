namespace Majik.Server.Composition;

/// <summary>
/// Server-side bot-brain selection, bound from the <c>Bot</c> configuration
/// section (env: <c>Bot__Strategy</c> etc.). This is the LIVE-FLIP lever for
/// the MCTS search bot.
///
/// <para>
/// <b>Default = <c>heuristic</c>:</b> dev, tests, and durable-log rehydration
/// replay (which RE-COMPUTES bot decisions and relies on their determinism —
/// see <c>MatchService.TryRehydrateAndDispatchAsync</c>) keep today's cheap
/// deterministic brain unless the deployment opts in.
/// </para>
///
/// <para>
/// <b><c>Strategy=mcts</c></b> installs <c>Majik.Bot.SearchStrategy</c> with
/// the parameters profiled in #2596 on the production shape (Render standard,
/// 1 vCPU / 2 GB): 150 iterations / 1500 ms wall-clock — ~115–127 iterations
/// actually complete per decision on one core, the same regime the
/// search-vs-heuristic strength gates (69–88% win rate) were measured at.
/// <see cref="InferOpponentArchetype"/> keeps the search HONEST against
/// humans: the opponent's archetype is inferred from their public cards and
/// hidden zones are determinized — never peeked. The K-world determinized
/// decision fits the same budget (measured 1.4–1.65 s on one core; world
/// materialization is ~1 ms/world).
/// </para>
///
/// <para>
/// <b>Known tradeoff (deliberate):</b> a wall-clock-capped search is not
/// deterministic across runs, so a bot match that has to be REHYDRATED after
/// a replica restart may fail to replay (the failure path is graceful — the
/// match is lost, never wedged). The heuristic default preserves replay
/// determinism everywhere else; revisit if bot-match rehydration losses show
/// up in practice (fix = persist bot decisions, or an iteration-bound
/// deterministic replay mode).
/// </para>
/// </summary>
public sealed class ServerBotOptions
{
    /// <summary>Configuration section name (<c>Bot</c>) this binds from.</summary>
    public const string SectionName = "Bot";

    /// <summary>
    /// Bot brain: <c>"heuristic"</c> (default) or <c>"mcts"</c>. Matches
    /// <c>Majik.Bot.BotPlayerAgent</c>'s strategy dispatch.
    /// </summary>
    public string Strategy { get; set; } = "heuristic";

    /// <summary>
    /// MCTS iteration cap per searched decision (only read when
    /// <see cref="Strategy"/> is <c>mcts</c>). 150 = the profiled live value.
    /// </summary>
    public int MaxMctsIterations { get; set; } = 150;

    /// <summary>
    /// MCTS wall-clock budget per searched decision in milliseconds (only read
    /// when <see cref="Strategy"/> is <c>mcts</c>). 1500 = the profiled live
    /// value; on 1 vCPU this is the binding limit (~120 iterations complete).
    /// </summary>
    public int MaxMctsBudgetMs { get; set; } = 1500;

    /// <summary>
    /// Honest hidden-information handling vs humans (only read when
    /// <see cref="Strategy"/> is <c>mcts</c>): infer the opponent's archetype
    /// from their public cards and determinize hidden zones across a belief —
    /// never peek at the real hand/library. Disable only for diagnostics; off
    /// means the search clones the live hidden zones (perfect-info = peeking).
    /// </summary>
    public bool InferOpponentArchetype { get; set; } = true;

    /// <summary>
    /// Process-wide cap on concurrent LIVE searches (env
    /// <c>Bot__SearchConcurrency</c>; only read when <see cref="Strategy"/> is
    /// <c>mcts</c>). Default 1: on the 1-vCPU prod box two overlapping ~1.5 s
    /// CPU-bound searches would split the core and each complete fewer
    /// iterations (weaker decisions + API latency) — gated searches QUEUE
    /// instead, so every search runs at full strength and a queued bot just
    /// thinks slightly longer (invisible vs humans). The wait is bounded
    /// (~10 s); on timeout that pick degrades to the heuristic decision. Must
    /// be &gt;= 1. Raise only on multi-core deployments.
    /// </summary>
    public int SearchConcurrency { get; set; } = 1;

    /// <summary>
    /// How far each MCTS rollout plays the sandbox out before evaluating (env
    /// <c>Bot__RolloutDepth</c>; only read when <see cref="Strategy"/> is
    /// <c>mcts</c>). One of <c>"LeafEval"</c> (no playout — eval at the
    /// decision point, the cheapest variant), <c>"EndOfTurn"</c> (remainder of
    /// the current turn only) or <c>"FullTurnPlus"</c> (current turn plus one
    /// full turn) — case-insensitive, validated against
    /// <see cref="Majik.Bot.Search.RolloutDepth"/> at registration. Default
    /// <c>"FullTurnPlus"</c> = today's behaviour; this is the #2596
    /// rollout-cost lever, flipped to a probe-gate winner via config only.
    /// </summary>
    public string RolloutDepth { get; set; } = "FullTurnPlus";

    /// <summary>
    /// Fail fast on a bad knob (called at registration so a typo'd env var
    /// crashes the boot, not the first vs-bot match creation).
    /// </summary>
    public void Validate()
    {
        if (Strategy is not ("heuristic" or "mcts"))
        {
            throw new ArgumentException(
                $"Unknown bot strategy '{Strategy}' — expected 'heuristic' or 'mcts' (Bot__Strategy).");
        }

        if (MaxMctsIterations <= 0)
        {
            throw new ArgumentException(
                $"Bot__MaxMctsIterations must be positive (got {MaxMctsIterations}).");
        }

        if (MaxMctsBudgetMs <= 0)
        {
            throw new ArgumentException(
                $"Bot__MaxMctsBudgetMs must be positive (got {MaxMctsBudgetMs}).");
        }

        if (SearchConcurrency < 1)
        {
            throw new ArgumentException(
                $"Bot__SearchConcurrency must be >= 1 (got {SearchConcurrency}).");
        }

        // Validate against the enum NAMES (case-insensitive) — never numeric
        // values — mirroring SearchStrategy.ParseRolloutDepth's fail-fast.
        if (!Enum.GetNames<Majik.Bot.Search.RolloutDepth>()
                .Any(n => n.Equals(RolloutDepth, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"Unknown Bot__RolloutDepth '{RolloutDepth}' — expected 'LeafEval', " +
                "'EndOfTurn' or 'FullTurnPlus'.");
        }
    }
}
