using Majik.Bot.Combat;
using Majik.Bot.Decks;
using Majik.Bot.Evaluation;
using Majik.Bot.Heuristic;
using Majik.Bot.OpponentModel;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;

namespace Majik.Bot.Search;

/// <summary>
/// MCTS-backed <see cref="IBotStrategy"/>. Uses <see cref="Mcts"/> to search
/// the best combat decision (DeclareAttackers / DeclareBlockers) and delegates
/// every other prompt to the inner <see cref="HeuristicStrategy"/>.
///
/// <para>
/// <b>InstanceId remap:</b> The MCTS search operates over sandbox clones of the
/// live game state, so the <see cref="SimMove"/> chosen by the search references
/// cloned <see cref="Creature"/> objects. Before returning a plan to the engine
/// these are translated back to the live engine objects by matching
/// <see cref="Creature.InstanceId"/> — the cloner preserves this value across
/// clones (see <c>GameStateCloner</c>). If any InstanceId is missing from the
/// live eligible set (shouldn't happen for a legal move) the fallback heuristic
/// picks for safety.
/// </para>
///
/// <para>YAGNI — only combat decisions are searched in this phase. All other
/// prompts delegate to the inner heuristic. Priority search is Task D2.</para>
/// </summary>
internal sealed class SearchStrategy : IBotStrategy
{
    private readonly HeuristicStrategy _heuristic;
    private readonly Mcts _mcts;

    /// <summary>
    /// The Mcts used for the K-world determinized loop, bounded PER WORLD to
    /// <see cref="_perWorldBudgetMs"/> (and a proportionally-scaled iteration cap)
    /// so K worlds × per-world ≈ the configured total budget. Non-null when EITHER
    /// determinized path is active — a known <see cref="_opponentDecklist"/> OR
    /// <see cref="_inferOpponent"/>; the perfect-info path always uses the original
    /// <see cref="_mcts"/> and is unaffected by this instance.
    /// </summary>
    private readonly Mcts? _determinizedMcts;

    private readonly ArchetypeWeights _weights;
    private readonly bool _prioritySearchEnabled;

    /// <summary>
    /// Root-level block search (default ON): <see cref="PickBlockers"/> runs
    /// MCTS rooted at the defender's block decision against the REAL declared
    /// attack via the engine's combat-state resume. Resolved ONCE from
    /// <see cref="BotConfig.RootBlockSearch"/> (null → true); false is the
    /// kill switch pinning the legacy <see cref="BlockCombatEval"/> path.
    /// </summary>
    private readonly bool _rootBlockSearch;

    /// <summary>
    /// The opponent's decklist when their archetype is known
    /// (<see cref="BotConfig.OpponentArchetype"/>), else null. Non-null selects
    /// the determinized K-world search path in <see cref="SearchRoot"/>; null keeps
    /// today's perfect-info single-tree search.
    /// </summary>
    private readonly IReadOnlyList<string>? _opponentDecklist;

    /// <summary>
    /// True when the bot must INFER the opponent's archetype from their public cards
    /// (<see cref="BotConfig.InferOpponentArchetype"/> set AND no explicit
    /// <see cref="BotConfig.OpponentArchetype"/>). Selects the belief-driven
    /// determinized path in <see cref="SearchRoot"/>; false keeps either the
    /// known-archetype determinized path (when <see cref="_opponentDecklist"/> is set)
    /// or today's perfect-info search.
    /// </summary>
    private readonly bool _inferOpponent;

    /// <summary>
    /// The archetype inferencer (idf precomputed once). Non-null only when
    /// <see cref="_inferOpponent"/> is true.
    /// </summary>
    private readonly ArchetypeInferencer? _inferencer;

    /// <summary>Total search budget (ms) handed to the K-world driver.</summary>
    private readonly int _totalBudgetMs;

    /// <summary>Reproducible base world seed for determinized sampling.</summary>
    private readonly int _baseSeed;

    /// <summary>
    /// Catastrophe threshold for the risk-aware two-tier vote, resolved ONCE from
    /// <see cref="BotConfig.RiskVoteThreshold"/> at construction (null → the
    /// <see cref="DeterminizedSearch.DefaultCatastropheThreshold"/> default;
    /// <see cref="double.NegativeInfinity"/> disables the filter). Threaded into
    /// BOTH determinized call sites in <see cref="SearchRoot"/>.
    /// </summary>
    private readonly double _riskThreshold;

    /// <summary>
    /// Per-world budget (ms) for the determinized driver, resolved ONCE from
    /// <see cref="BotConfig.PerWorldBudgetMs"/> at construction (null →
    /// <see cref="DefaultPerWorldBudgetMs"/> = 400, today's behaviour). The total
    /// budget is split across K worlds (K = round(total / perWorld), clamped
    /// 1..<see cref="_kMax"/> inside <see cref="DeterminizedSearch"/>), so a larger
    /// total — or a SMALLER per-world budget — yields more worlds, not longer
    /// per-world searches.
    ///
    /// <para>
    /// Each per-world <see cref="Mcts.SearchWithStats"/> call is ALSO bounded to this
    /// value (see <see cref="_determinizedMcts"/> / <see cref="DeterminizedConfigFrom"/>),
    /// so the K worlds genuinely SPLIT the total budget — total wall-clock ≈
    /// K × perWorld ≈ the configured total (modulo the K clamp). It does NOT
    /// run K full-budget searches.
    /// </para>
    /// </summary>
    private readonly int _perWorldBudgetMs;

    /// <summary>
    /// Upper clamp on the determinized world count K, resolved ONCE from
    /// <see cref="BotConfig.MaxWorlds"/> at construction (null →
    /// <see cref="DefaultKMax"/> = 8, today's behaviour — matching
    /// <see cref="DeterminizedSearch.Run"/> / <see cref="DeterminizedSearch.RunBelief"/>'s
    /// default kMax). Threaded through both determinized paths so the K-world budget
    /// split is identical whether the archetype is known or inferred.
    /// </summary>
    private readonly int _kMax;

    /// <summary>The per-world budget <see cref="BotConfig.PerWorldBudgetMs"/> = null
    /// resolves to — today's 400 ms split.</summary>
    internal const int DefaultPerWorldBudgetMs = 400;

    /// <summary>The world-count clamp <see cref="BotConfig.MaxWorlds"/> = null
    /// resolves to — today's kMax of 8.</summary>
    internal const int DefaultKMax = 8;

    /// <summary>
    /// Map a <see cref="BotConfig"/> to a sensible <see cref="MctsConfig"/>.
    ///
    /// <para>
    /// <c>DepthTurns=1</c> is the Stage-A finding: deeper rollouts wash out
    /// because the heuristic strategy recovers most positions to a similar
    /// score, while shallower trees finish faster and converge more reliably
    /// within the iteration budget. MaxIterations is capped at 200 to keep
    /// combat decisions within the engine's synchronous call budget.
    /// </para>
    ///
    /// <para>
    /// <c>RolloutDepth</c> parses <see cref="BotConfig.RolloutDepth"/>
    /// case-insensitively (null = <see cref="RolloutDepth.FullTurnPlus"/>, the
    /// byte-identical default); an unknown value throws here so a typo fails
    /// fast at construction, mirroring the strategy-name validation. Internal
    /// so tests can assert the exact BotConfig → MctsConfig threading.
    /// </para>
    ///
    /// <para>
    /// <c>TreeStateReuse</c> resolves <see cref="BotConfig.TreeStateReuse"/>
    /// (null = off, the byte-identical default) — the equivalence-gated
    /// snapshot/restore lever (see <see cref="MctsConfig.TreeStateReuse"/>).
    /// </para>
    /// </summary>
    internal static MctsConfig ConfigFrom(BotConfig bot) => new(
        MaxIterations: bot.MaxMctsIterations ?? 200,
        MaxMillis: bot.MaxMctsBudgetMs ?? 1500,
        DepthTurns: 1,
        ExplorationC: 1.41,
        RolloutDepth: ParseRolloutDepth(bot.RolloutDepth),
        TreeStateReuse: bot.TreeStateReuse ?? false);

    /// <summary>
    /// Resolve <see cref="BotConfig.RolloutDepth"/> (string?, the config-surface
    /// shape) to the <see cref="RolloutDepth"/> enum: null →
    /// <see cref="RolloutDepth.FullTurnPlus"/> (today's behaviour); enum NAMES
    /// match case-insensitively; anything else (including bare numerics) throws
    /// an <see cref="ArgumentException"/> NAMING the bad value — fail-fast at
    /// construction, mirroring the strategy-name validation.
    /// </summary>
    internal static RolloutDepth ParseRolloutDepth(string? value) => value switch
    {
        null => RolloutDepth.FullTurnPlus,
        _ when value.Equals(nameof(RolloutDepth.LeafEval), StringComparison.OrdinalIgnoreCase)
            => RolloutDepth.LeafEval,
        _ when value.Equals(nameof(RolloutDepth.EndOfTurn), StringComparison.OrdinalIgnoreCase)
            => RolloutDepth.EndOfTurn,
        _ when value.Equals(nameof(RolloutDepth.FullTurnPlus), StringComparison.OrdinalIgnoreCase)
            => RolloutDepth.FullTurnPlus,
        _ => throw new ArgumentException(
            $"Unknown RolloutDepth '{value}' — expected 'LeafEval', 'EndOfTurn' or 'FullTurnPlus'."),
    };

    /// <summary>
    /// Derive the PER-WORLD MctsConfig for the determinized K-world loop from the
    /// perfect-info <paramref name="full"/> config. Both bounds present in
    /// <see cref="MctsConfig"/> are split so K worlds genuinely divide the total
    /// budget instead of each running the full search:
    /// <list type="bullet">
    ///   <item><c>MaxMillis</c> → <paramref name="perWorldBudgetMs"/> (the wall-clock
    ///     bound; K worlds × perWorld ≈ full.MaxMillis since K ≈ full.MaxMillis/perWorld).</item>
    ///   <item><c>MaxIterations</c> → scaled by the <c>perWorld / total</c> fraction
    ///     (min 1) so an iteration-bounded config also splits across worlds rather
    ///     than running the full iteration count K times.</item>
    /// </list>
    /// <c>DepthTurns</c> / <c>ExplorationC</c> / <c>RolloutDepth</c> /
    /// <c>TreeStateReuse</c> are preserved unchanged — every per-world search
    /// INHERITS the configured rollout depth and reuse mode.
    /// </summary>
    internal static MctsConfig DeterminizedConfigFrom(MctsConfig full, int perWorldBudgetMs)
    {
        // Iteration split: same perWorld/total fraction as the time split, floored at 1
        // so a tiny budget still searches at least one iteration per world.
        var total = full.MaxMillis;
        var scaledIterations = total <= 0
            ? full.MaxIterations
            : Math.Max(1, (int)Math.Round((double)full.MaxIterations * perWorldBudgetMs / total));

        // preserves: DepthTurns, ExplorationC, RolloutDepth, TreeStateReuse; splits: MaxMillis, MaxIterations
        return full with
        {
            MaxMillis = perWorldBudgetMs,
            MaxIterations = scaledIterations,
        };
    }

    /// <summary>
    /// Concurrency gate for the TOP-LEVEL live searches (null = ungated, the
    /// default). Resolved from <see cref="BotConfig.SearchConcurrency"/> to the
    /// process-wide <see cref="SearchConcurrencyGate"/> so overlapping searches
    /// from concurrent matches queue instead of splitting the CPU. Held only
    /// around <see cref="SearchRoot"/> at the decision entries — never inside
    /// <see cref="EngineSimulator"/> rollouts (nested within a held permit).
    /// </summary>
    private readonly SearchGate? _gate;

    public SearchStrategy(BotConfig config)
        : this(config, ResolveGate(config))
    {
    }

    /// <summary>
    /// Test seam: inject an ISOLATED gate so gate tests cannot interfere with
    /// the process-wide shared gate (or each other). Production always goes
    /// through the public constructor → <see cref="ResolveGate"/>.
    /// </summary>
    internal SearchStrategy(BotConfig config, SearchGate? gate)
    {
        ArgumentNullException.ThrowIfNull(config);
        _gate = gate;
        _heuristic = new HeuristicStrategy(config);
        // WeightsOverride: use explicit vector when provided; fall back to the
        // archetype lookup so default behavior is completely unchanged.
        _weights = config.WeightsOverride ?? ArchetypeWeights.ForArchetype(config.ArchetypeName);
        _prioritySearchEnabled = config.PrioritySearchEnabled;
        _rootBlockSearch = config.RootBlockSearch ?? true;
        var mctsConfig = ConfigFrom(config);
        var sim = new EngineSimulator(_weights, deck: null); // Task 5 wires the real deck strategy
        _mcts = new Mcts(sim, mctsConfig);

        // OpponentArchetype: resolve the decklist ONCE up front. A known archetype
        // selects determinized search; null = perfect-info (the production-safe
        // default). An unknown name throws here (BotDeckCatalog.Get) so a typo
        // fails fast at construction rather than silently degrading to perfect-info.
        _opponentDecklist = config.OpponentArchetype is { } a
            ? BotDeckCatalog.Get(a)
            : null;

        // InferOpponentArchetype: only the "honest-vs-human" inference path when no
        // explicit archetype is given (an explicit OpponentArchetype takes precedence
        // — we never override a known opponent with a guess). Build the inferencer
        // ONCE up front (idf precomputed) so per-decision inference is cheap.
        _inferOpponent = config.InferOpponentArchetype && config.OpponentArchetype is null;
        _inferencer = _inferOpponent ? new ArchetypeInferencer() : null;

        // World-split knobs: resolve the nullable config once up front (null →
        // today's 400 ms / kMax 8 — byte-identical defaults). K derives from the
        // budget split: clamp(round(total / perWorld), 1, kMax).
        _perWorldBudgetMs = config.PerWorldBudgetMs ?? DefaultPerWorldBudgetMs;
        _kMax = config.MaxWorlds ?? DefaultKMax;

        // Determinized total budget reuses the same wall-clock budget the MCTS
        // config already computes (MaxMctsBudgetMs ?? 1500). The K-world driver
        // splits this total across worlds via _perWorldBudgetMs, and the per-world
        // Mcts it runs (_determinizedMcts) is bounded to _perWorldBudgetMs PER WORLD
        // (not the full total), so K worlds × per-world ≈ the total budget — NOT
        // K full searches. Built whenever a determinized path is active (known
        // archetype OR inference); the perfect-info path keeps using _mcts untouched.
        _totalBudgetMs = mctsConfig.MaxMillis;
        _determinizedMcts = (_opponentDecklist is null && !_inferOpponent)
            ? null
            : new Mcts(sim, DeterminizedConfigFrom(mctsConfig, _perWorldBudgetMs));

        // Fixed, config-derived base seed → determinized runs are reproducible.
        _baseSeed = config.RandomSeed;

        // RiskVoteThreshold: resolve the nullable knob once up front (null → the
        // DeterminizedSearch default; -inf is the kill switch that disables the
        // risk filter) so SearchRoot threads a plain double per decision.
        _riskThreshold = ResolveRiskThreshold(config.RiskVoteThreshold);
    }

    /// <summary>
    /// Resolve <see cref="BotConfig.SearchConcurrency"/> to the effective gate:
    /// null (the default) → no gate, today's ungated behaviour — unit tests,
    /// the PARALLEL strength probes, and sim-internal agents must never
    /// serialize; non-null → the process-wide shared gate (first-configured
    /// permit count wins; see <see cref="SearchConcurrencyGate.Shared"/>).
    /// </summary>
    private static SearchGate? ResolveGate(BotConfig? config) =>
        config?.SearchConcurrency is { } permits ? SearchConcurrencyGate.Shared(permits) : null;

    /// <summary>The resolved gate (test instrumentation — null means ungated).</summary>
    internal SearchGate? Gate => _gate;

    /// <summary>The resolved per-world budget (test instrumentation —
    /// <see cref="BotConfig.PerWorldBudgetMs"/> ?? <see cref="DefaultPerWorldBudgetMs"/>).</summary>
    internal int PerWorldBudgetMsResolved => _perWorldBudgetMs;

    /// <summary>The resolved world-count clamp (test instrumentation —
    /// <see cref="BotConfig.MaxWorlds"/> ?? <see cref="DefaultKMax"/>).</summary>
    internal int KMaxResolved => _kMax;

    /// <summary>The per-world Mcts's config (test instrumentation — null when no
    /// determinized path is active). Pins that the per-world search is genuinely
    /// bounded to the configured split.</summary>
    internal MctsConfig? DeterminizedMctsConfig => _determinizedMcts?.Config;

    /// <summary>
    /// Resolve <see cref="BotConfig.RiskVoteThreshold"/> to the effective
    /// catastrophe threshold: null → <see cref="DeterminizedSearch.DefaultCatastropheThreshold"/>;
    /// any explicit value (including <see cref="double.NegativeInfinity"/>, the
    /// filter kill switch) passes through unchanged.
    /// </summary>
    internal static double ResolveRiskThreshold(double? cfg) =>
        cfg ?? DeterminizedSearch.DefaultCatastropheThreshold;

    /// <summary>
    /// Runs the root search: determinized K-world search when the opponent's
    /// archetype is known, else today's perfect-info single-tree
    /// <see cref="Mcts.Search"/>. Returns a <see cref="SimMove"/> of the SAME shape
    /// either way — a representative move carrying sandbox object refs — so the
    /// callers' existing InstanceId / Player.Id remap handles both paths unchanged.
    /// </summary>
    private SimMove SearchRoot(SimState root, GameContext ctx, Player self)
    {
        // (1) Known archetype — UNCHANGED. Determinize against the known decklist and
        // run the K-world summed-robust-child driver.
        if (_opponentDecklist is { } deck)
        {
            var determinized = root.WithDeterminization(deck, worldSeed: _baseSeed);
            // _determinizedMcts is bounded to _perWorldBudgetMs PER WORLD, so the
            // K-world loop SPLITS the total budget (K × per-world ≈ total) instead
            // of running K full-budget searches.
            return DeterminizedSearch.Run(
                _determinizedMcts!,   // non-null whenever _opponentDecklist is set
                determinized,
                totalBudgetMs: _totalBudgetMs,
                perWorldBudgetMs: _perWorldBudgetMs,
                kMax: _kMax,
                catastropheThreshold: _riskThreshold);
        }

        // (2) Inference — "honest-vs-human". Read the opponent's public cards, infer
        // a belief over archetypes, allocate worlds across the top-M, and run the
        // belief-driven determinized driver. baseRoot here is the PLAIN capture root
        // (NOT pre-determinized) — RunBelief attaches each world's decklist + seed.
        if (_inferOpponent)
        {
            var publicCards = OpponentPublicCardNames(ctx, self);
            var belief = _inferencer!.Infer(publicCards);
            int k = DeterminizedSearch.KFor(_totalBudgetMs, _perWorldBudgetMs, _kMax);
            var alloc = WorldAllocator.Allocate(belief, k, topM: 4)
                .Select(x => ((IReadOnlyList<string>)BotDeckCatalog.Get(x.Archetype), x.Worlds))
                .ToList();
            // Defensive: Infer always returns a normalized belief (the metagame prior when
            // no public cards are seen) and KFor is always >= 1, so a non-empty allocation
            // is the norm — this guard only fires if the archetype catalog is empty or k<=0.
            // Fall back to perfect-info search rather than routing an empty allocation
            // through RunBelief (which requires a non-empty one).
            if (alloc.Count == 0) return _mcts.Search(root);
            return DeterminizedSearch.RunBelief(
                _determinizedMcts!,   // non-null whenever _inferOpponent is true
                root,
                alloc,
                publicCards,
                totalBudgetMs: _totalBudgetMs,
                perWorldBudgetMs: _perWorldBudgetMs,
                kMax: _kMax,
                catastropheThreshold: _riskThreshold);
        }

        return _mcts.Search(root);   // perfect-info, unchanged
    }

    /// <summary>
    /// The opponent's PUBLIC card names — battlefield + graveyard + exile of the
    /// non-self player (the zones a human opponent has visibly revealed). Hidden zones
    /// (hand / library) are deliberately NOT read: inference works only from what the
    /// opponent has shown. Returns empty when there is no distinct opponent.
    /// </summary>
    private static IReadOnlyList<string> OpponentPublicCardNames(GameContext ctx, Player self)
    {
        var opp = ctx.AllPlayers.FirstOrDefault(p => !ReferenceEquals(p, self));
        if (opp is null) return Array.Empty<string>();

        return opp.Zones.Battlefield.GetCards()
            .Concat(opp.Zones.Graveyard.GetCards())
            .Concat(opp.Zones.Exile.GetCards())
            .Select(c => c.Name)
            .ToList();
    }

    // ── Combat decisions — run the search ─────────────────────────────────────

    /// <inheritdoc/>
    public CombatPlan PickAttackers(GameContext ctx, Player self, IReadOnlyList<Creature> eligible)
    {
        if (eligible.Count == 0)
            return CombatPlan.None;

        // Capture root state. GameContext.CurrentPhase is a StepStateType (fine-grained
        // step), but SimState wants a PhaseStateType (coarse phase). The engine has
        // already entered combat; resume from the Combat phase so the sandbox picks up
        // the BeginningOfCombat window and then surfaces DeclareAttackers.
        var root = SimState.Capture(
            livePlayers: ctx.AllPlayers,
            activePlayer: ctx.ActivePlayer,
            turnNumber: ctx.TurnNumber,
            phase: PhaseStateType.Combat,
            searchedSeat: self);

        // Concurrency gate (live bots only — null gate is the ungated default).
        // Starvation guard: a bounded wait that times out degrades THIS pick to
        // the heuristic decision instead of stalling the match indefinitely.
        if (_gate is { } gate && !gate.TryEnter())
            return _heuristic.PickAttackers(ctx, self, eligible);

        SimMove chosen;
        try
        {
            chosen = SearchRoot(root, ctx, self);
        }
        catch
        {
            // Any search failure (e.g. terminal root) → fall back to heuristic.
            return _heuristic.PickAttackers(ctx, self, eligible);
        }
        finally
        {
            // Only reached after a successful TryEnter (the timeout path
            // returned above), so the release is always balanced.
            _gate?.Exit();
        }

        // MCTS may return a Priority action (not a CombatPlan) when priority search
        // is enabled and the root decision surfaced as a priority window
        // (e.g. BeginningOfCombat) rather than DeclareAttackers. In that case
        // the search correctly chose the best priority action, but we still need
        // to decide on attackers — fall back to the heuristic for that decision
        // rather than returning CombatPlan.None (which silently skips the attack).
        if (chosen.CombatPlan == null)
            return _heuristic.PickAttackers(ctx, self, eligible);

        // Explicit empty-attack: search deliberately chose to not attack.
        if (chosen.IsEmptyAttack)
            return CombatPlan.None;

        // ── InstanceId remap ─────────────────────────────────────────────────
        // The chosen plan's Creature references are sandbox clones. Re-map them
        // to the LIVE eligible creatures by InstanceId so the engine accepts them.
        var liveById = eligible.ToDictionary(c => c.InstanceId);
        var remapped = new List<AttackerDeclaration>(chosen.CombatPlan.Attackers.Count);

        foreach (var decl in chosen.CombatPlan.Attackers)
        {
            if (!liveById.TryGetValue(decl.Attacker.InstanceId, out var liveAttacker))
            {
                // Stale InstanceId — remap failed. Fall back to heuristic for safety.
                return _heuristic.PickAttackers(ctx, self, eligible);
            }

            // Resolve the defending player: find the live player by Id in AllPlayers.
            object defender;
            if (decl.DefendingPlayerOrPlaneswalker is Player sandboxPlayer)
            {
                var liveDefender = ctx.AllPlayers.FirstOrDefault(p => p.Id == sandboxPlayer.Id);
                // preserves: liveAttacker, decl.DefendingPlayerOrPlaneswalker (identity mapped)
                defender = liveDefender ?? ctx.AllPlayers.First(p => !ReferenceEquals(p, self));
            }
            else
            {
                defender = ctx.AllPlayers.First(p => !ReferenceEquals(p, self));
            }

            remapped.Add(new AttackerDeclaration(liveAttacker, defender));
        }

        return remapped.Count == 0 ? CombatPlan.None : new CombatPlan(remapped);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Root-level block search (default ON, <see cref="BotConfig.RootBlockSearch"/>):
    /// MCTS rooted at the defender's block decision against the REAL declared
    /// attack. The historical D1.5 problem — a sandbox resumed at Combat re-ran
    /// the opponent's attack declaration from scratch, so search could never
    /// see the real attack — is solved by the engine's combat-state resume:
    /// the root <see cref="SimState"/> carries the live attack as an Id-level
    /// <see cref="CombatResumeState"/> and every sandbox enters combat PAST
    /// the declaration at the blocker ask (CR 509).
    ///
    /// <para>Fallback guard (the D1.5 lesson): when the search fails, is
    /// gated out, or its chosen root move is not a block plan (a defender
    /// priority window with real options surfaced first — the search answered
    /// a different question), fall back to <see cref="EvalBlocks"/> — the
    /// legacy <see cref="BlockCombatEval"/> path, which also remains the
    /// rollout-opponent block policy and the <c>RootBlockSearch=false</c>
    /// kill-switch behavior.</para>
    /// </remarks>
    public BlockPlan PickBlockers(
        GameContext ctx,
        Player self,
        IReadOnlyList<Creature> attackers,
        IReadOnlyList<Creature> eligible)
    {
        if (attackers.Count == 0 || eligible.Count == 0)
            return BlockPlan.None;

        if (!_rootBlockSearch)
            return EvalBlocks(attackers, eligible, self);

        var root = SimState.Capture(
            livePlayers: ctx.AllPlayers,
            activePlayer: ctx.ActivePlayer,          // the ATTACKING player
            turnNumber: ctx.TurnNumber,
            phase: PhaseStateType.Combat,
            searchedSeat: self,                      // the DEFENDER
            preDeclaredAttack: Majik.Core.Combat.CombatResumeState.FromAttackers(attackers, self));

        // Concurrency gate (live bots only — null gate is the ungated default).
        // Starvation guard: a bounded wait that times out degrades THIS pick to
        // the eval decision instead of stalling the match indefinitely.
        if (_gate is { } gate && !gate.TryEnter())
            return EvalBlocks(attackers, eligible, self);

        SimMove chosen;
        try
        {
            chosen = SearchRoot(root, ctx, self);
        }
        catch
        {
            // Any search failure → fall back to the eval path for correctness.
            return EvalBlocks(attackers, eligible, self);
        }
        finally
        {
            // Only reached after a successful TryEnter (the timeout path
            // returned above), so the release is always balanced.
            _gate?.Exit();
        }

        // D1.5 guard, block edition: a non-block root decision means the
        // search answered a different question — fall back rather than
        // misapply it.
        if (chosen.BlockPlan == null)
            return EvalBlocks(attackers, eligible, self);

        // Explicit empty plan = the search deliberately chose NOT to block
        // (distinct from null above).
        if (chosen.BlockPlan.Blockers.Count == 0)
            return BlockPlan.None;

        // ── InstanceId remap ─────────────────────────────────────────────────
        // The chosen plan's Creature references are sandbox clones; remap both
        // ends of every pair to the LIVE creatures. RemapBlockPlan degrades to
        // BlockPlan.None when every pair drops (stale ids — shouldn't happen
        // for a legal move): treat that as a remap failure and fall back.
        var remapped = SearchAgent.RemapBlockPlan(chosen.BlockPlan, attackers, eligible);
        return remapped.Blockers.Count == 0
            ? EvalBlocks(attackers, eligible, self)
            : remapped;
    }

    /// <summary>
    /// The legacy block decision — direct combat-outcome scoring over the
    /// enriched candidate set (chump, trade, gang blocks), lethal-aware: a
    /// plan that lets lethal through scores <c>double.MinValue</c>, so any
    /// survival block beats taking lethal damage. Serves as the
    /// <see cref="PickBlockers"/> fallback floor (search failure / non-block
    /// root decision / remap failure) and the <c>RootBlockSearch=false</c>
    /// kill-switch path; also what rollout opponents keep using.
    /// </summary>
    private BlockPlan EvalBlocks(
        IReadOnlyList<Creature> attackers,
        IReadOnlyList<Creature> eligible,
        Player self)
    {
        // Enumerate the enriched candidate set (includes chump, trade, gang blocks).
        var candidates = BlockCombatEval.EnumeratePlans(attackers, eligible);

        // Score each candidate and pick the best.
        return BlockCombatEval.PickBest(candidates, attackers, self.LifeTotal, _weights);
    }

    // ── Priority decisions — MCTS-backed (Task D2) ────────────────────────────

    /// <summary>
    /// Selects a priority action using MCTS when there are real choices;
    /// short-circuits to Pass when only Pass is legal (avoids wasted search).
    ///
    /// <para>
    /// <b>InstanceId remap:</b> The MCTS search operates over sandbox clones.
    /// The chosen <see cref="SimMove"/> carries a <see cref="PriorityAction"/>
    /// that references cloned objects. This method remaps them back to the
    /// LIVE objects by InstanceId before returning.
    /// </para>
    ///
    /// <para>
    /// Supported with full remap: <c>PlayLand</c>, <c>Pass</c>.
    /// <c>CastSpell</c> and <c>ActivateAbility</c> fall back to the inner
    /// heuristic when found as the chosen action (Phase 1 scope — complex
    /// targeting / ability remaps deferred).
    /// </para>
    /// </summary>
    public PriorityAction PickPriorityAction(GameContext ctx, Player self)
    {
        // Short-circuit: if priority MCTS is disabled, delegate to the inner heuristic.
        // Used in tests where sandbox games hit the priority-loop-safety limit,
        // making each MCTS call prohibitively slow (e.g., unimplemented Burn cards).
        if (!_prioritySearchEnabled)
            return _heuristic.PickPriorityAction(ctx, self);

        // Defensive guard: if the player has already lost, do not launch search.
        // The engine's PriorityLoop now skips lost players before reaching this
        // method (CR 800.4a fix), but this guard provides an additional safety
        // layer in case the strategy is called from another code path or tests.
        if (self.HasLost)
            return PriorityAction.Pass;

        // Step 1 — enumerate legal actions.
        var legal = LegalActionEnumerator.ForPriority(ctx, self);

        // Step 2 — short-circuit: if only Pass is legal (or empty), return Pass.
        if (legal.Count <= 1)
            return PriorityAction.Pass;

        // Step 3 — build SimState and run MCTS.
        // CurrentPhase is StepStateType (fine-grained step). Convert to
        // PhaseStateType (coarse phase) for SimState. Default to PreCombatMain.
        var phase = ctx.CurrentPhase?.ToPhaseStateType() ?? PhaseStateType.PreCombatMain;

        SimState root;
        try
        {
            root = SimState.Capture(
                livePlayers: ctx.AllPlayers,
                activePlayer: ctx.ActivePlayer,
                turnNumber: ctx.TurnNumber,
                phase: phase,
                searchedSeat: self);
        }
        catch
        {
            return _heuristic.PickPriorityAction(ctx, self);
        }

        // Concurrency gate (live bots only — null gate is the ungated default).
        // Starvation guard: bounded wait → heuristic fallback, never a stall.
        if (_gate is { } gate && !gate.TryEnter())
            return _heuristic.PickPriorityAction(ctx, self);

        SimMove chosen;
        try
        {
            chosen = SearchRoot(root, ctx, self);
        }
        catch
        {
            // Any search failure → fall back to heuristic for correctness.
            return _heuristic.PickPriorityAction(ctx, self);
        }
        finally
        {
            // Only reached after a successful TryEnter (the timeout path
            // returned above), so the release is always balanced.
            _gate?.Exit();
        }

        // Step 4 — map the chosen SimMove back to a LIVE PriorityAction by InstanceId.
        return RemapPriorityAction(chosen, ctx, self);
    }

    /// <summary>
    /// Remaps a <see cref="SimMove"/>'s <see cref="PriorityAction"/> from
    /// sandbox-cloned objects to the LIVE engine objects by InstanceId.
    /// Falls back to heuristic on any remap failure.
    /// </summary>
    private PriorityAction RemapPriorityAction(SimMove chosen, GameContext ctx, Player self)
    {
        var action = chosen.PriorityAction;
        if (action == null)
            return _heuristic.PickPriorityAction(ctx, self);

        return action switch
        {
            PriorityAction.PassAction =>
                // Pass needs no remap.
                PriorityAction.Pass,

            PriorityAction.PlayLand pl =>
                // Find the live land in self's hand by InstanceId.
                RemapPlayLand(pl, ctx, self),

            PriorityAction.CastSpell cs =>
                // Remap the sandbox card to the LIVE card by InstanceId.
                // LegalActionEnumerator always creates CastSpell with empty
                // targets (Array.Empty<object>()); the PriorityLoop's cast
                // dispatcher (TurnDriver.DispatchCast) prompts the agent for
                // targets at cast time via ChooseTargetsAsync — so we do not
                // need to remap targets here. For safety, if targets are
                // non-empty (future extension) we fall through to heuristic
                // for any target that cannot be remapped.
                RemapCastSpell(cs, ctx, self),

            PriorityAction.ActivateAbility =>
                // ActivateAbility remap deferred to Phase 2 (ability lookup
                // by source InstanceId is non-trivial).
                // Fall back to heuristic for correctness.
                _heuristic.PickPriorityAction(ctx, self),

            _ =>
                // Unknown/unexpected action kind → heuristic fallback.
                _heuristic.PickPriorityAction(ctx, self),
        };
    }

    /// <summary>
    /// Remaps a <see cref="PriorityAction.CastSpell"/> from the sandbox clone
    /// to the live card in <paramref name="self"/>'s hand by InstanceId.
    ///
    /// <para>
    /// <b>Target remap:</b> <see cref="LegalActionEnumerator.ForPriority"/> always
    /// emits <c>CastSpell</c> with <c>Array.Empty&lt;object&gt;()</c> targets.
    /// The live engine's cast dispatcher (<c>TurnDriver.DispatchCast</c>) then
    /// calls <see cref="IPlayerAgent.ChooseTargetsAsync"/> to gather real targets
    /// at cast time, delegated to <see cref="SearchStrategy.PickTargets"/> (which
    /// calls the inner heuristic's <see cref="Heuristic.TargetPolicy"/>). So we
    /// pass through the empty target list as-is — target selection happens
    /// naturally post-cast, not here.
    /// </para>
    ///
    /// <para>
    /// If targets were non-empty (possible in a future extension where the
    /// enumerator pre-selects targets), we attempt a best-effort remap:
    ///   • <see cref="Majik.Core.Cards.Permanent"/> / <see cref="ICard"/>: match
    ///     by <c>InstanceId</c> across all live zones (hand + battlefield of both players).
    ///   • <see cref="Player"/>: match by <c>Player.Id</c>.
    ///   • Unknown type: fall back to heuristic for safety.
    /// </para>
    ///
    /// <para>Falls back to heuristic if the live card is not found in hand.</para>
    /// </summary>
    private PriorityAction RemapCastSpell(
        PriorityAction.CastSpell sandboxAction,
        GameContext ctx,
        Player self)
    {
        // Find the live card in self's hand by InstanceId.
        var liveCard = self.Zones.Hand.GetCards()
            .FirstOrDefault(c => c.InstanceId == sandboxAction.Card.InstanceId);

        if (liveCard == null)
        {
            // InstanceId not found — sandbox state diverged or card left hand.
            // Fall back to heuristic for correctness.
            return _heuristic.PickPriorityAction(ctx, self);
        }

        // Fast path: no targets to remap (the common case — LegalActionEnumerator
        // always creates CastSpell with empty targets).
        if (sandboxAction.Targets.Count == 0)
        {
            // preserves: liveCard (remapped by InstanceId), HoldPriority, AlternativeCost, AdditionalCosts
            return new PriorityAction.CastSpell(
                liveCard,
                Array.Empty<object>(),
                sandboxAction.HoldPriority,
                sandboxAction.AlternativeCost,
                sandboxAction.AdditionalCosts);
        }

        // Non-empty targets: attempt best-effort remap by InstanceId / Player.Id.
        // Build lookup maps for live objects across all zones of all players.
        var liveCardsById = ctx.AllPlayers
            .SelectMany(p =>
                p.Zones.Hand.GetCards()
                    .Concat(p.Zones.Battlefield.GetCards())
                    .Concat(p.Zones.Graveyard.GetCards()))
            .GroupBy(c => c.InstanceId)
            .ToDictionary(g => g.Key, g => g.First());

        var livePlayersById = ctx.AllPlayers.ToDictionary(p => p.Id);

        var remappedTargets = new List<object>(sandboxAction.Targets.Count);
        foreach (var target in sandboxAction.Targets)
        {
            switch (target)
            {
                case ICard targetCard:
                    if (liveCardsById.TryGetValue(targetCard.InstanceId, out var liveTarget))
                        remappedTargets.Add(liveTarget);
                    else
                        // Target not found in live zones — fall back to heuristic.
                        return _heuristic.PickPriorityAction(ctx, self);
                    break;
                case Player targetPlayer:
                    if (livePlayersById.TryGetValue(targetPlayer.Id, out var livePlayer))
                        remappedTargets.Add(livePlayer);
                    else
                        return _heuristic.PickPriorityAction(ctx, self);
                    break;
                default:
                    // Unknown target type — can't remap safely. Fall back to heuristic.
                    return _heuristic.PickPriorityAction(ctx, self);
            }
        }

        // preserves: liveCard, remappedTargets, HoldPriority, AlternativeCost, AdditionalCosts
        return new PriorityAction.CastSpell(
            liveCard,
            remappedTargets,
            sandboxAction.HoldPriority,
            sandboxAction.AlternativeCost,
            sandboxAction.AdditionalCosts);
    }

    /// <summary>
    /// Remaps a <see cref="PriorityAction.PlayLand"/> from the sandbox clone
    /// to the live land in self's hand by InstanceId. Falls back to heuristic
    /// if the land is not found or if the live context does not permit a land play.
    ///
    /// <para>
    /// The live-context check (<c>ctx.LandPlayAvailable</c>) is critical: the MCTS
    /// sandbox may generate PlayLand actions in a sandbox turn where the searched
    /// player IS active and in a main phase, even if the LIVE game is in a priority
    /// window where a land play is illegal (e.g. opponent's turn, combat step, or
    /// the land drop has already been used this live turn). Without this guard, the
    /// MCTS returns PlayLand and the live PriorityLoop rejects it ~50k times per
    /// 20-game run — a spin loop that saturates the priority action cap and forces
    /// nearly every game to a draw by turn cap.
    /// </para>
    /// </summary>
    private PriorityAction RemapPlayLand(
        PriorityAction.PlayLand sandboxAction,
        GameContext ctx,
        Player self)
    {
        // Guard: live context must allow a land play. If not, the MCTS suggested
        // a land play that is valid in the SANDBOX turn but invalid in the LIVE
        // game (wrong phase, opponent's turn, or drop already used). Fall back to
        // heuristic so the live engine doesn't reject and re-prompt endlessly.
        if (!ctx.LandPlayAvailable)
            return _heuristic.PickPriorityAction(ctx, self);

        var liveLand = self.Zones.Hand.GetCards()
            .FirstOrDefault(c => c.InstanceId == sandboxAction.Land.InstanceId);

        if (liveLand == null)
        {
            // InstanceId not found — sandbox state diverged. Fall back to heuristic.
            return _heuristic.PickPriorityAction(ctx, self);
        }

        // preserves: liveLand (remapped), HoldPriority (from sandbox action)
        return new PriorityAction.PlayLand(liveLand, HoldPriority: sandboxAction.HoldPriority);
    }

    // ── All other decisions — delegate to heuristic ────────────────────────────

    public MulliganDecision PickMulligan(IReadOnlyList<ICard> hand, int mulligansTaken)
        => _heuristic.PickMulligan(hand, mulligansTaken);

    public IReadOnlyList<ICard> PickCardsToBottom(IReadOnlyList<ICard> hand, int countToBottom)
        => _heuristic.PickCardsToBottom(hand, countToBottom);

    public IReadOnlyList<object> PickTargets(GameContext ctx, Player self, TargetRequest request)
        => _heuristic.PickTargets(ctx, self, request);

    public int PickX(GameContext ctx, Player self)
        => _heuristic.PickX(ctx, self);

    public int PickMode(GameContext ctx, Player self, IReadOnlyList<string> modes)
        => _heuristic.PickMode(ctx, self, modes);

    public ManaPayment PickMana(GameContext ctx, Player self, ManaCost cost)
        => _heuristic.PickMana(ctx, self, cost);

    public IReadOnlyList<ITriggeredAbility> OrderTriggers(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine)
        => _heuristic.OrderTriggers(ctx, mine);

    public Majik.Core.Keywords.ScryAction.ScryDecision PickScry(
        GameContext? ctx, Player self, IReadOnlyList<ICard> peeked)
        => _heuristic.PickScry(ctx, self, peeked);

    public Majik.Core.Keywords.SurveilAction.SurveilDecision PickSurveil(
        GameContext? ctx, Player self, IReadOnlyList<ICard> peeked)
        => _heuristic.PickSurveil(ctx, self, peeked);

    public ICard? PickLibraryCard(GameContext? ctx, Player self, IReadOnlyList<ICard> candidates, string kindLabel)
        => _heuristic.PickLibraryCard(ctx, self, candidates, kindLabel);
}
