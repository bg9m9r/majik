namespace Majik.Server.Composition;

/// <summary>
/// Server-side bot-brain selection, bound from the <c>Bot</c> configuration
/// section (env: <c>Bot__Strategy</c> etc.). This is the LIVE-FLIP lever for
/// the MCTS search bot.
///
/// <para>
/// <b>Default = <c>heuristic</c>:</b> dev and tests keep today's cheap
/// deterministic brain unless the deployment opts in (a cost / playtest
/// default).
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
    /// Tree-state reuse inside each MCTS search (env
    /// <c>Bot__TreeStateReuse</c>; only read when <see cref="Strategy"/> is
    /// <c>mcts</c>). When true the UCT loop snapshot-caches tree-node states
    /// and expands / rolls out from the nearest cached ancestor instead of
    /// replaying the whole root path — iteration-for-iteration EQUIVALENT to
    /// the root-replay loop (equivalence-gated), only cheaper per iteration.
    /// Default <c>false</c> = today's behaviour; this is the tree-reuse
    /// lever, flipped to a probe-gate winner via config only. A non-boolean
    /// env value fails fast at registration (the config-binder conversion
    /// error crashes the boot, mirroring the other knobs' fail-fast).
    /// </summary>
    public bool TreeStateReuse { get; set; }

    /// <summary>
    /// Root-level block search (env <c>Bot__RootBlockSearch</c>; only read
    /// when <see cref="Strategy"/> is <c>mcts</c>). When true (the default —
    /// this lever ships ON), <c>SearchStrategy.PickBlockers</c> runs MCTS
    /// rooted at the defender's block decision against the REAL declared
    /// attack via the engine's combat-state resume (CR 509), falling back to
    /// the legacy <c>BlockCombatEval</c> path on any failure. <c>false</c> is
    /// the kill switch pinning the legacy eval path permanently. A
    /// non-boolean env value fails fast at registration (the config-binder
    /// conversion error crashes the boot, mirroring <see cref="TreeStateReuse"/>).
    /// </summary>
    public bool RootBlockSearch { get; set; } = true;

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
    }
}
