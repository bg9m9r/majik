using System.Diagnostics;
using FluentAssertions;
using Majik.Bot.Combat;
using Majik.Bot.Decks;
using Majik.Bot.Evaluation;
using Majik.Bot.Heuristic;
using Majik.Bot.Search;
using Majik.Bot.Tests.Integration.Helpers;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Simulation;
using Majik.Core.StateMachine;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Search;

/// <summary>
/// On-demand DECISION-COST PROFILER for the MCTS search bot — measurement only,
/// no behavior under test. Quantifies where one live "mcts" decision's wall-clock
/// and allocations actually go on a realistic mid-game board, to size the live
/// flip on the 1-vCPU / 2 GB production instance:
///
/// <list type="number">
///   <item><c>GameStateCloner.Clone(players)</c> — the per-iteration floor.</item>
///   <item><c>SandboxGame.From(...)</c> — clone + fresh subsystems + spell resolver.</item>
///   <item><c>EngineSimulator.Advance</c> (path 0/1/2) and <c>Rollout</c> (depth 1).</item>
///   <item>Full <c>Mcts.SearchWithStats</c> decisions at 150it/1500ms, 50/500, 30/300.</item>
///   <item>Determinized K-world decision (<c>DeterminizedSearch.Run</c>, Burn decklist)
///     + standalone world materialization (clone + <c>DeterminizationSampler.Resample</c>).</item>
///   <item><c>BoardEval.Score</c> leaf cost.</item>
///   <item>Rollout-policy split: <c>HeuristicStrategy.PickPriorityAction</c> and
///     <c>CombatSearch.FindBestAttackPlan</c> standalone (no swappable rollout-policy
///     seam exists in <c>EngineSimulator</c> — by design we measure, not build one).</item>
/// </list>
///
/// <para>
/// The board is REAL: a heuristic-vs-heuristic Prowess/Burn game driven to a turn
/// cap via <see cref="GameFacade"/> + <see cref="DeckLoader.LoadReal"/>, then captured
/// as a <see cref="SimState"/> at the searched seat's pre-combat main — the same
/// decision class <c>SearchStrategy.PickPriorityAction</c> searches live.
/// </para>
///
/// <para>
/// Output: grep-able <c>[PROF]</c> lines to the xUnit sink AND appended live to
/// <c>/tmp/majik-bot-profile.log</c> (override via <c>MAJIK_BOT_PROFILE_LOG</c>;
/// tag runs via <c>MAJIK_PROF_LABEL</c>, e.g. <c>Release-pinned-c0</c> under
/// <c>taskset -c 0</c>). Medians over warm reps; JIT/repo warm-up happens before
/// any timed region.
/// </para>
/// </summary>
public sealed class DecisionProfileTests
{
    private readonly ITestOutputHelper _output;

    public DecisionProfileTests(ITestOutputHelper output) => _output = output;

    private static readonly string LogPath =
        Environment.GetEnvironmentVariable("MAJIK_BOT_PROFILE_LOG") ?? "/tmp/majik-bot-profile.log";

    private void Log(string line)
    {
        _output.WriteLine(line);
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        }
        catch
        {
            // best-effort live stream — never fail the measurement on IO
        }
    }

    // ── Stat helpers ──────────────────────────────────────────────────────────

    private static double Median(List<double> xs)
    {
        var s = xs.OrderBy(x => x).ToList();
        int n = s.Count;
        return n == 0 ? double.NaN : (n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2.0);
    }

    /// <summary>µs-scale op: per-rep Stopwatch + per-THREAD allocation delta.</summary>
    private static (double MedianUs, double MedianBytes) MeasureMicro(int warmup, int reps, Action op)
    {
        for (int i = 0; i < warmup; i++) op();
        var times = new List<double>(reps);
        var allocs = new List<double>(reps);
        for (int i = 0; i < reps; i++)
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            long t0 = Stopwatch.GetTimestamp();
            op();
            times.Add(Stopwatch.GetElapsedTime(t0).TotalMicroseconds);
            allocs.Add(GC.GetAllocatedBytesForCurrentThread() - a0);
        }
        return (Median(times), Median(allocs));
    }

    /// <summary>ms-scale op: per-rep Stopwatch + PROCESS-WIDE allocation delta
    /// (engine continuations may not all stay on this thread).</summary>
    private static (double MedianMs, double MedianBytes) MeasureMilli(int warmup, int reps, Action op)
    {
        for (int i = 0; i < warmup; i++) op();
        var times = new List<double>(reps);
        var allocs = new List<double>(reps);
        for (int i = 0; i < reps; i++)
        {
            long a0 = GC.GetTotalAllocatedBytes(precise: true);
            long t0 = Stopwatch.GetTimestamp();
            op();
            times.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            allocs.Add(GC.GetTotalAllocatedBytes(precise: true) - a0);
        }
        return (Median(times), Median(allocs));
    }

    private static double WorkingSetMb()
    {
        using var p = Process.GetCurrentProcess();
        p.Refresh();
        return p.WorkingSet64 / (1024.0 * 1024.0);
    }

    private static double PeakWorkingSetMb()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            p.Refresh();
            return p.PeakWorkingSet64 / (1024.0 * 1024.0);
        }
        catch
        {
            return double.NaN;
        }
    }

    private static string Kb(double bytes) => $"{bytes / 1024.0:F1}KB";
    private static string Mb(double bytes) => $"{bytes / (1024.0 * 1024.0):F2}MB";

    // ── Realistic mid-game board ──────────────────────────────────────────────

    /// <summary>
    /// Drives heuristic-vs-heuristic Prowess (Alice) vs Burn (Bob) games to a turn
    /// cap and returns the MEATIEST undecided board (most creatures + developed
    /// lands + live hands) — so the profiled decision has real battlefield
    /// presence, spent-but-real hands, and live libraries. The facade's players
    /// ARE the mid-game state.
    /// </summary>
    private static async Task<(GameFacade Facade, int TurnsPlayed)> BuildMidGameBoardAsync()
    {
        (GameFacade Facade, int Turns, int Score)? best = null;

        foreach (var (maxTurns, seed) in new[]
                 { (6, 4242), (6, 4243), (7, 4244), (6, 4245), (7, 4246), (5, 4250) })
        {
            var facade = GameFacade.Create(
                aliceName: "Alice",
                bobName: "Bob",
                aliceDeck: DeckLoader.LoadReal("Prowess", ProbeHarness.Repo),
                bobDeck: DeckLoader.LoadReal("Burn", ProbeHarness.Repo),
                cardRepo: ProbeHarness.Repo);

            facade.ReplaceAliceAgent(new BotPlayerAgent(
                facade.Alice, new BotConfig("Prowess", RandomSeed: seed)));
            facade.ReplaceBobAgent(new BotPlayerAgent(
                facade.Bob, new BotConfig("Burn", RandomSeed: seed + 500)));

            Majik.Core.Game.GameDriver.GameResult result;
            try
            {
                await facade.StartFullGameAsync(
                    maxTurns: maxTurns, ct: CancellationToken.None, rng: new GameRandom(seed));
                result = await facade.FullGameTask!;
            }
            catch
            {
                continue; // engine crash on this seed (same class ProbeHarness counts Inconclusive) — try the next
            }

            if (result.Winner is not null)
                continue; // decided — not a usable mid-game root

            int Creatures(Player p) => p.Zones.Battlefield.GetCards().OfType<Creature>().Count();
            int Lands(Player p) => p.Zones.Battlefield.GetCards().OfType<Land>().Count();
            int Hand(Player p) => p.Zones.Hand.GetCards().Count();

            // Weighted board "meatiness": the searched seat's creatures matter most
            // (they unlock the combat measurements), then overall development.
            int score =
                Creatures(facade.Alice) * 5 + Creatures(facade.Bob) * 3 +
                Math.Min(Lands(facade.Alice), 6) + Math.Min(Lands(facade.Bob), 6) +
                Math.Min(Hand(facade.Alice), 4) + Math.Min(Hand(facade.Bob), 4);

            if (best is null || score > best.Value.Score)
                best = (facade, result.TurnsPlayed, score);
        }

        if (best is null)
            throw new InvalidOperationException(
                "Could not produce an undecided mid-game board — every warm-up game was decided before its cap.");

        return (best.Value.Facade, best.Value.Turns);
    }

    private static string SeatSummary(Player p)
    {
        var bf = p.Zones.Battlefield.GetCards().ToList();
        return $"life={p.LifeTotal} bf={bf.Count} (cre={bf.OfType<Creature>().Count()} " +
               $"land={bf.OfType<Land>().Count()}) hand={p.Zones.Hand.GetCards().Count()} " +
               $"lib={p.Zones.Library.GetCards().Count()} gy={p.Zones.Graveyard.GetCards().Count()}";
    }

    // ── THE profiler ──────────────────────────────────────────────────────────

    [Fact(Skip = "on-demand diagnostic — un-skip to run")]
    public async Task ProfileBotDecisionCosts()
    {
        var label = Environment.GetEnvironmentVariable("MAJIK_PROF_LABEL") ?? "unlabeled";
        var buildCfg =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        Log($"[PROF] run label={label} build={buildCfg} cpus={Environment.ProcessorCount} " +
            $"gcServer={System.Runtime.GCSettings.IsServerGC} pid={Environment.ProcessId}");

        // ── Setup (untimed): warm the embedded repo + build the real board ────
        _ = SharedCardData.Repo.GetByName("Lightning Bolt"); // force lazy 22k-row seed load
        var (facade, turnsPlayed) = await BuildMidGameBoardAsync();

        // Snapshot = start of Alice's next turn, post-untap, at pre-combat main —
        // the live decision point SearchStrategy.PickPriorityAction searches.
        foreach (var perm in facade.Alice.Zones.Battlefield.GetCards().OfType<Permanent>())
            if (perm.IsTapped) perm.Untap();

        var players = new List<Player> { facade.Alice, facade.Bob };
        int rootTurn = turnsPlayed + 1;
        var root = SimState.Capture(
            livePlayers: players,
            activePlayer: facade.Alice,
            turnNumber: rootTurn,
            phase: PhaseStateType.PreCombatMain,
            searchedSeat: facade.Alice);

        Log($"[PROF] board turn={rootTurn} alice[Prowess] {SeatSummary(facade.Alice)}");
        Log($"[PROF] board turn={rootTurn} bob[Burn]      {SeatSummary(facade.Bob)}");

        var weights = ArchetypeWeights.ForArchetype("Prowess");
        var sim = new EngineSimulator(weights); // same construction as SearchStrategy

        // Root decision shape (also JIT-warms the whole Advance path once).
        var rootDecision = sim.Advance(root, Array.Empty<SimMove>());
        rootDecision.IsTerminal.Should().BeFalse("the captured board must not be terminal");
        var rootKeys = string.Join(", ", rootDecision.LegalMoves.Take(8).Select(m => m.Key));
        Log($"[PROF] root kind={rootDecision.Kind} legalMoves={rootDecision.LegalMoves.Count} keys=[{rootKeys}]");
        rootDecision.LegalMoves.Count.Should().BeGreaterThan(1,
            "a forced root short-circuits Mcts.SearchWithStats — the profile needs a real branching decision");

        // ── 1. GameStateCloner.Clone ───────────────────────────────────────────
        {
            var (us, bytes) = MeasureMicro(warmup: 5, reps: 30, () => GameStateCloner.Clone(players));
            Log($"[PROF] clone n=30 median={us:F0}µs alloc={Kb(bytes)}");
        }

        // ── 2. SandboxGame.From (clone + subsystems + resolver) ───────────────
        {
            var (us, bytes) = MeasureMicro(warmup: 5, reps: 30, () =>
                SandboxGame.From(
                    players,
                    new GameRandom(42),
                    p => new SearchAgent(p),
                    cardRepo: SharedCardData.Repo));
            Log($"[PROF] sandboxFrom n=30 median={us:F0}µs alloc={Kb(bytes)}");
        }

        // ── 3. Advance (path 0/1/2) + Rollout (depth 1, live default) ─────────
        var pathMove1 = rootDecision.LegalMoves.FirstOrDefault(m => !m.IsPass) ?? rootDecision.LegalMoves[0];
        var path1 = new[] { pathMove1 };
        var afterMove1 = sim.Advance(root, path1);
        SimMove[]? path2 = null;
        if (!afterMove1.IsTerminal && afterMove1.LegalMoves.Count > 0)
        {
            var m2 = afterMove1.LegalMoves.FirstOrDefault(m => !m.IsPass) ?? afterMove1.LegalMoves[0];
            path2 = new[] { pathMove1, m2 };
        }

        {
            var (ms, bytes) = MeasureMilli(warmup: 2, reps: 10, () => sim.Advance(root, Array.Empty<SimMove>()));
            Log($"[PROF] advance pathLen=0 n=10 median={ms:F2}ms alloc={Mb(bytes)}");
        }
        {
            var (ms, bytes) = MeasureMilli(warmup: 2, reps: 10, () => sim.Advance(root, path1));
            Log($"[PROF] advance pathLen=1 move={pathMove1.Key} n=10 median={ms:F2}ms alloc={Mb(bytes)}");
        }
        if (path2 is not null)
        {
            var (ms, bytes) = MeasureMilli(warmup: 2, reps: 10, () => sim.Advance(root, path2));
            Log($"[PROF] advance pathLen=2 move2={path2[1].Key} n=10 median={ms:F2}ms alloc={Mb(bytes)}");
        }

        {
            var (ms, bytes) = MeasureMilli(warmup: 2, reps: 10, () => sim.Rollout(root, Array.Empty<SimMove>(), depthTurns: 1));
            Log($"[PROF] rollout depth=1 pathLen=0 n=10 median={ms:F2}ms alloc={Mb(bytes)}");
        }
        {
            var (ms, bytes) = MeasureMilli(warmup: 2, reps: 10, () => sim.Rollout(root, path1, depthTurns: 1));
            Log($"[PROF] rollout depth=1 pathLen=1 n=10 median={ms:F2}ms alloc={Mb(bytes)}");
        }

        // ── 6. BoardEval.Score (leaf cost) ─────────────────────────────────────
        {
            var bus = new EventBus();
            var stack = new Majik.Core.Stack.Stack(bus);
            var evalCtx = new GameContext(
                self: facade.Alice, allPlayers: players, activePlayer: facade.Alice,
                turnNumber: rootTurn, currentPhase: StepStateType.PreCombatMain, stack: stack);
            var (us, bytes) = MeasureMicro(warmup: 50, reps: 200,
                () => BoardEval.Score(evalCtx, facade.Alice, weights));
            Log($"[PROF] boardEval n=200 median={us:F1}µs alloc={Kb(bytes)}");
        }

        // ── 7. Rollout-policy cost split (standalone heuristic pieces) ────────
        // EngineSimulator has no swappable rollout-policy seam (HeuristicStrategy is
        // constructed inside RolloutCoreUnsafe), so per the measurement-only rule we
        // time the policy's two big pieces standalone on the live board instead.
        {
            var bus = new EventBus();
            var stack = new Majik.Core.Stack.Stack(bus);
            var ctx = new GameContext(
                self: facade.Alice, allPlayers: players, activePlayer: facade.Alice,
                turnNumber: rootTurn, currentPhase: StepStateType.PreCombatMain, stack: stack,
                landPlayAvailable: true);
            var heuristic = new HeuristicStrategy(new BotConfig(ArchetypeName: "Burn")); // same as rollout's
            var (us, bytes) = MeasureMicro(warmup: 5, reps: 20,
                () => heuristic.PickPriorityAction(ctx, facade.Alice));
            Log($"[PROF] heurPickPriority n=20 median={us:F0}µs alloc={Kb(bytes)}");

            var eligible = facade.Alice.Zones.Battlefield.GetCards()
                .OfType<Creature>().Where(c => c.Power > 0).ToList();
            if (eligible.Count > 0)
            {
                var (cus, cbytes) = MeasureMicro(warmup: 5, reps: 20,
                    () => CombatSearch.FindBestAttackPlan(ctx, facade.Alice, eligible, weights, budgetMs: 20));
                Log($"[PROF] combatSearch eligible={eligible.Count} budget=20ms n=20 median={cus:F0}µs alloc={Kb(cbytes)}");
            }
            else
            {
                Log("[PROF] combatSearch skipped — searched seat has no eligible creatures on this board");
            }
        }

        // ── 4. Full Mcts decisions at candidate live configs ──────────────────
        foreach (var (iters, budgetMs) in new[] { (150, 1500), (50, 500), (30, 300) })
        {
            var mcts = new Mcts(sim, new MctsConfig(
                MaxIterations: iters, MaxMillis: budgetMs, DepthTurns: 1, ExplorationC: 1.41));
            mcts.SearchWithStats(root); // warm rep (untimed)
            for (int rep = 1; rep <= 3; rep++)
            {
                double wsBefore = WorkingSetMb();
                long a0 = GC.GetTotalAllocatedBytes(precise: true);
                long t0 = Stopwatch.GetTimestamp();
                var result = mcts.SearchWithStats(root);
                double wallMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                long alloc = GC.GetTotalAllocatedBytes(precise: true) - a0;
                int done = result.RootStats.Sum(s => s.Visits);
                Log($"[PROF] decision cfg={iters}it/{budgetMs}ms rep={rep} wall={wallMs:F0}ms " +
                    $"iters={done} ms/iter={(done > 0 ? wallMs / done : double.NaN):F1} " +
                    $"allocMB={alloc / (1024.0 * 1024.0):F0} best={result.Best.Key} " +
                    $"ws={wsBefore:F0}->{WorkingSetMb():F0}MB");
            }
        }

        // ── 4a. Per-RolloutDepth decision cells (the #2596 truncation lever) ──
        // Same board, same 150it/1500ms shape as the live default cell above,
        // but with the rollout narrowed (EndOfTurn = current-turn boundary,
        // LeafEval = no playout, BoardEval at the decision point). Quantifies
        // the realized ms/iter multiple — LeafEval still pays clone +
        // drive-to-decision per iteration, so expect well above the naive
        // 6 ms → 13 µs eval-only ratio.
        foreach (var depth in new[] { RolloutDepth.LeafEval, RolloutDepth.EndOfTurn })
        {
            var mcts = new Mcts(sim, new MctsConfig(
                MaxIterations: 150, MaxMillis: 1500, DepthTurns: 1, ExplorationC: 1.41,
                RolloutDepth: depth));
            mcts.SearchWithStats(root); // warm rep (untimed)
            for (int rep = 1; rep <= 3; rep++)
            {
                double wsBefore = WorkingSetMb();
                long a0 = GC.GetTotalAllocatedBytes(precise: true);
                long t0 = Stopwatch.GetTimestamp();
                var result = mcts.SearchWithStats(root);
                double wallMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                long alloc = GC.GetTotalAllocatedBytes(precise: true) - a0;
                int done = result.RootStats.Sum(s => s.Visits);
                Log($"[PROF] decisionDepth cfg=150it/1500ms depth={depth} rep={rep} wall={wallMs:F0}ms " +
                    $"iters={done} ms/iter={(done > 0 ? wallMs / done : double.NaN):F2} " +
                    $"allocMB={alloc / (1024.0 * 1024.0):F0} best={result.Best.Key} " +
                    $"ws={wsBefore:F0}->{WorkingSetMb():F0}MB");
            }
        }

        // ── 4a2. Tree-state reuse cells (the snapshot/restore lever) ──────────
        // Same board, reuse OFF vs ON at two shapes:
        //   live     — 150it/1500ms, the production cell: realized ms/iter +
        //              alloc delta when the iteration cap binds.
        //   capacity — 2000it/1500ms, wall-clock-bound: how many iterations
        //              the LIVE budget fits in each mode (the iters@1500ms
        //              number the reuse strength probes raise their cap to).
        foreach (var reuse in new[] { false, true })
        {
            foreach (var (cell, iters) in new[] { ("live", 150), ("live800", 800), ("capacity", 2000) })
            {
                var mcts = new Mcts(sim, new MctsConfig(
                    MaxIterations: iters, MaxMillis: 1500, DepthTurns: 1, ExplorationC: 1.41,
                    TreeStateReuse: reuse));
                mcts.SearchWithStats(root); // warm rep (untimed)
                for (int rep = 1; rep <= 3; rep++)
                {
                    double wsBefore = WorkingSetMb();
                    long a0 = GC.GetTotalAllocatedBytes(precise: true);
                    long t0 = Stopwatch.GetTimestamp();
                    var result = mcts.SearchWithStats(root);
                    double wallMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                    long alloc = GC.GetTotalAllocatedBytes(precise: true) - a0;
                    int done = result.RootStats.Sum(s => s.Visits);
                    Log($"[PROF] decisionReuse cell={cell} reuse={(reuse ? "on" : "off")} " +
                        $"cfg={iters}it/1500ms rep={rep} wall={wallMs:F0}ms " +
                        $"iters={done} ms/iter={(done > 0 ? wallMs / done : double.NaN):F2} " +
                        $"allocMB={alloc / (1024.0 * 1024.0):F0} best={result.Best.Key} " +
                        $"ws={wsBefore:F0}->{WorkingSetMb():F0}MB");
                }
            }
        }

        // ── 4b. Combat-phase decision at the live default (context) ───────────
        {
            var combatRoot = SimState.Capture(
                livePlayers: players, activePlayer: facade.Alice, turnNumber: rootTurn,
                phase: PhaseStateType.Combat, searchedSeat: facade.Alice);
            var combatDecision = sim.Advance(combatRoot, Array.Empty<SimMove>());
            if (!combatDecision.IsTerminal && combatDecision.LegalMoves.Count > 1)
            {
                var mcts = new Mcts(sim, new MctsConfig(150, 1500, DepthTurns: 1, ExplorationC: 1.41));
                long t0 = Stopwatch.GetTimestamp();
                var result = mcts.SearchWithStats(combatRoot);
                double wallMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                int done = result.RootStats.Sum(s => s.Visits);
                Log($"[PROF] decisionCombat cfg=150it/1500ms kind={combatDecision.Kind} " +
                    $"legal={combatDecision.LegalMoves.Count} wall={wallMs:F0}ms iters={done} " +
                    $"ms/iter={(done > 0 ? wallMs / done : double.NaN):F1} best={result.Best.Key}");
            }
            else
            {
                Log($"[PROF] decisionCombat skipped — combat root forced/terminal " +
                    $"(terminal={combatDecision.IsTerminal} legal={combatDecision.LegalMoves.Count})");
            }
        }

        // ── 5. Determinized variant (K-world split, Burn decklist) ────────────
        var burnDeck = BotDeckCatalog.Get("Burn");
        {
            // Standalone world-materialization cost (mirrors EngineSimulator.ResolveCloneSource):
            // one clone of the live players + one seeded hidden-zone resample with prod-built cards.
            int matSeed = 0;
            var (cloneMs, _) = MeasureMilli(warmup: 2, reps: 10, () => GameStateCloner.Clone(players));
            var (matMs, matBytes) = MeasureMilli(warmup: 2, reps: 10, () =>
            {
                var worldBase = GameStateCloner.Clone(players);
                DeterminizationSampler.Resample(
                    worldBase.Players, root.SearchedSeatId, burnDeck, worldSeed: matSeed++,
                    observedPublic: null,
                    buildCard: (name, owner) => DeckCardBuilder.Build(
                        name, owner, SharedCardData.Repo,
                        new ReplacementBus(), new ContinuousEffectsService(),
                        triggers: null, zones: null, eventBus: null,
                        routeThroughNamedFactories: true));
            });
            Log($"[PROF] worldMaterialize n=10 cloneOnly={cloneMs:F2}ms cloneAndResample={matMs:F2}ms " +
                $"resampleDelta={matMs - cloneMs:F2}ms alloc={Mb(matBytes)}");

            // Full determinized decision exactly as SearchStrategy's known-archetype path
            // wires it: per-world Mcts bounded to 400 ms, K = KFor(1500, 400, 8).
            var perWorldCfg = SearchStrategy.DeterminizedConfigFrom(
                new MctsConfig(150, 1500, DepthTurns: 1, ExplorationC: 1.41), perWorldBudgetMs: 400);
            var detMcts = new Mcts(sim, perWorldCfg);
            int k = DeterminizedSearch.KFor(1500, 400, 8);
            DeterminizedSearch.Run(detMcts, root.WithDeterminization(burnDeck, worldSeed: 9_000),
                totalBudgetMs: 1500); // warm rep (untimed)
            for (int rep = 1; rep <= 3; rep++)
            {
                var detRoot = root.WithDeterminization(burnDeck, worldSeed: 10_000 + rep * 100);
                long a0 = GC.GetTotalAllocatedBytes(precise: true);
                long t0 = Stopwatch.GetTimestamp();
                var move = DeterminizedSearch.Run(detMcts, detRoot, totalBudgetMs: 1500);
                double wallMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                long alloc = GC.GetTotalAllocatedBytes(precise: true) - a0;
                Log($"[PROF] decisionDet cfg={perWorldCfg.MaxIterations}it/{perWorldCfg.MaxMillis}ms-per-world " +
                    $"k={k} total=1500ms rep={rep} wall={wallMs:F0}ms " +
                    $"allocMB={alloc / (1024.0 * 1024.0):F0} best={move.Key}");
            }
        }

        Log($"[PROF] peakWS={PeakWorkingSetMb():F0}MB finalWS={WorkingSetMb():F0}MB");
        Log("[PROF] done");
    }
}
