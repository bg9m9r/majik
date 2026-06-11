using FluentAssertions;
using Majik.Bot.Search;
using Majik.Bot.Tests.Integration.Helpers;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Random;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Search;

/// <summary>
/// Shared harness for the on-demand strength probes (the <c>*Probe</c> classes in
/// this folder). Each probe class is ONE head-to-head wrapped in its own xUnit
/// test class — and therefore its own test COLLECTION — so un-skipping several
/// probes in one <c>dotnet test</c> invocation runs them CONCURRENTLY (xUnit
/// parallelizes collections within an assembly; this project has no
/// <c>CollectionBehavior</c> override, so the default applies with
/// maxParallelThreads = logical CPU count).
///
/// <para>
/// <b>Probe families.</b> Two families share this harness:
/// <list type="bullet">
///   <item><b>Asymmetric inference probes</b> (<see cref="InferVsHeuristicProbe"/>,
///     <see cref="InferVsPerfectInfoProbe"/>, <see cref="PerfectInfoVsHeuristicProbe"/>,
///     <see cref="KnownDetVsHeuristicProbe"/>) — bot under test plays
///     <see cref="AsymmetricBotDeck"/> (Prowess), opponent plays
///     <see cref="AsymmetricOppDeck"/> (Burn). Summary tag <c>[INFER]</c>.
///     Interpretation guidance lives in <c>InferenceProbes.cs</c>.</item>
///   <item><b>Mirror determinization probes</b> (<see cref="DetVsHeuristicProbe"/>,
///     <see cref="DetVsPerfectInfoProbe"/>, <see cref="MirrorPerfectInfoVsHeuristicProbe"/>)
///     — both seats play <see cref="MirrorArchetype"/> (Prowess). Summary tag
///     <c>[DET]</c>. Interpretation guidance lives in <c>DeterminizedProbes.cs</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Interpretation contract (all probes — no hard win-rate assertion).</b>
/// Every probe ASSERTS ONLY liveness: it RAN end-to-end, decided at least one
/// game, and produced a finite win-rate in [0,1]. It does NOT assert a win%
/// threshold — the controller un-skips the probes, tails
/// <c>/tmp/majik-probe-progress.log</c> (via <see cref="ProbeProgress"/>), and
/// makes the strength judgment from the <c>[STRENGTH]</c> / <c>[INFER]</c> /
/// <c>[DET]</c> summary lines.
/// </para>
///
/// <para>
/// <b>Deck choice — Prowess (bot) / Burn (opponent).</b> Both archetypes are
/// exercised by the perfect-info strength harness
/// (<see cref="SearchVsHeuristicTests"/>) without sim-clone trouble, both are in
/// the inferencer's candidate set (<see cref="Majik.Bot.OpponentModel.ArchetypeInferencer"/>
/// over <c>BotDeckCatalog.Archetypes</c>) and the metagame prior, and both clone
/// cleanly under <c>DeckLoader.LoadReal</c> — neither hits the
/// <c>BrainstormTemplate</c> / library-reorder <c>CloneForSim</c> gap that bespoke
/// card-draw spells can. The asymmetric pair has genuine hidden-information
/// relevance — the opponent's Burn hand (reach / direct damage) changes Prowess's
/// correct race / combat math — and the Prowess mirror likewise (the opponent's
/// hand of pump / burn changes the right combat math).
/// </para>
/// </summary>
internal static class ProbeHarness
{
    /// <summary>
    /// Embedded card repository — loaded once for all probes. LoadReal resolves
    /// card names to proper typed shells so non-basic lands tap for mana and the
    /// board develops normally (mirrors <see cref="SearchVsHeuristicTests"/>).
    /// </summary>
    internal static readonly EmbeddedCardRepository Repo = new();

    /// <summary>
    /// Games per head-to-head. Default kept modest so each probe is bounded
    /// wall-clock at the configured budget. The controller may bump this for a
    /// meatier measurement.
    /// </summary>
    internal const int Games = 30;

    /// <summary>MCTS iteration cap per search call — matches the production
    /// default. This is the BINDING limit for every search (see
    /// <see cref="MctsBudgetMs"/>).</summary>
    internal const int MctsIterations = 150;

    /// <summary>
    /// Wall-clock budget per MCTS search call (ms) — deliberately GENEROUS.
    /// The iteration cap (<see cref="MctsIterations"/> = 150) is the binding
    /// limit for every search; this wall-clock ceiling exists ONLY so that CPU
    /// contention from probe classes running in PARALLEL (one xUnit collection
    /// per probe) can never truncate a search before it completes its
    /// iterations. If searches were wall-clock-bound, contention would silently
    /// shrink them and distort the measured strength numbers — iteration-bound
    /// searches keep the comparison fair regardless of how many probes share
    /// the machine.
    /// <para>
    /// NOTE: strength numbers from this iteration-bound regime are NOT directly
    /// comparable to the old 1500 ms wall-clock-bound runs — each run carries
    /// its own baseline head-to-head (perfect-info-vs-heuristic) for exactly
    /// that reason.
    /// </para>
    /// For the determinized / inference bots this TOTAL is split across the
    /// sampled worlds by <see cref="Majik.Bot.Search.DeterminizedSearch"/>; the
    /// perfect-info bot spends it all on its single tree.
    /// </summary>
    internal const int MctsBudgetMs = 6000;

    /// <summary>
    /// Wall-clock budget per MCTS search call (ms) for the ROLLOUT-DEPTH MATRIX
    /// cells (<c>RolloutDepthMatrixProbes.cs</c>) — the LIVE production budget.
    /// Unlike <see cref="MctsBudgetMs"/> these cells are deliberately
    /// wall-clock-BOUND at 1500 ms: the matrix's question is whether a cheaper
    /// <see cref="Majik.Bot.Search.RolloutDepth"/> converts the SAME live budget
    /// into more useful iterations (the iteration cap per cell sets how many it
    /// may spend; whether the budget lets it finish them is part of the
    /// measurement).
    /// </summary>
    internal const int MatrixBudgetMs = 1500;

    /// <summary>Maximum turns per game — prevents hangs on drawn-out games.</summary>
    internal const int MaxTurns = 30;

    /// <summary>Base seed for the ASYMMETRIC (inference) probe family; each
    /// head-to-head uses its own +1000 block so the four see different game
    /// seeds, and game i within a head-to-head uses <c>seedBlock + i</c>.</summary>
    internal const int AsymmetricBaseSeed = 7000;

    /// <summary>Base seed for the MIRROR (determinization) probe family —
    /// distinct from <see cref="AsymmetricBaseSeed"/> blocks per head-to-head.</summary>
    internal const int MirrorBaseSeed = 5000;

    /// <summary>The archetype the BOT UNDER TEST plays in the asymmetric family.
    /// It knows only its OWN deck; it must INFER the opponent's.</summary>
    internal const string AsymmetricBotDeck = "Prowess";

    /// <summary>The archetype the OPPONENT plays in the asymmetric family —
    /// DIFFERENT from <see cref="AsymmetricBotDeck"/>. The inference bot must
    /// converge on this from the opponent's public cards.</summary>
    internal const string AsymmetricOppDeck = "Burn";

    /// <summary>Mirror archetype for the determinization family (both seats).</summary>
    internal const string MirrorArchetype = "Prowess";

    /// <summary>Base seed for the MIRROR rollout-depth MATRIX cells
    /// (<c>RolloutDepthMatrixProbes.cs</c>) — each cell uses its own +1000
    /// block, all distinct from the existing families' blocks.</summary>
    internal const int MatrixMirrorBaseSeed = 20000;

    /// <summary>Base seed for the ASYMMETRIC rollout-depth MATRIX cells —
    /// each cell uses its own +1000 block.</summary>
    internal const int MatrixAsymmetricBaseSeed = 30000;

    /// <summary>Base seed for the MIRROR tree-state-reuse cells
    /// (<c>TreeReuseProbes.cs</c>) — each cell uses its own +1000 block,
    /// distinct from every other family's blocks.</summary>
    internal const int ReuseMirrorBaseSeed = 40000;

    /// <summary>Base seed for the ASYMMETRIC tree-state-reuse cells —
    /// each cell uses its own +1000 block.</summary>
    internal const int ReuseAsymmetricBaseSeed = 50000;

    /// <summary>Base seed for the MIRROR world-split (K-tuning) cells
    /// (<c>WorldSplitProbes.cs</c>). Deliberately SHARED by both cells —
    /// unlike every other family's +1000-per-cell blocks — so the K=8 head
    /// and its K=4 control play the SAME game seeds (same decks, same
    /// shuffles, same heuristic opponent): a paired comparison in which only
    /// the world split differs. Distinct from every other family's blocks.</summary>
    internal const int WorldSplitMirrorBaseSeed = 60000;

    /// <summary>Base seed for the MIRROR cap-raise (iteration-cap tuning)
    /// cells (<c>CapRaiseProbes.cs</c>). Deliberately SHARED by both cells —
    /// like <see cref="WorldSplitMirrorBaseSeed"/> — so the cap=1200 head and
    /// its cap=800 control play the SAME game seeds (same decks, same
    /// shuffles, same heuristic opponent): a paired comparison in which only
    /// the iteration cap differs. Distinct from every other family's blocks.</summary>
    internal const int CapRaiseMirrorBaseSeed = 80000;

    /// <summary>Base seed for the ASYMMETRIC heuristic-vs-heuristic ANCHOR
    /// (<c>AnchorProbes.cs</c>) — heuristic Prowess vs heuristic Burn, the
    /// matchup baseline the MCTS asymmetric numbers are read against. Distinct
    /// from every other family's blocks.</summary>
    internal const int AnchorBaseSeed = 70000;

    /// <summary>Which seat's strategy won a single game (or a draw / crash).</summary>
    internal enum SeatAWinner { SeatA, SeatB, Draw, Inconclusive }

    // ── BotConfig factories — ASYMMETRIC (inference) family ─────────────────────
    // The "bot under test" seat plays AsymmetricBotDeck (Prowess). The "opponent"
    // seat plays AsymmetricOppDeck (Burn). Each factory takes (seed) and is paired
    // with the correct DECK by RunHeadToHead — the deck travels with the strategy.

    /// <summary>Inference = mcts, OpponentArchetype null, InferOpponentArchetype
    /// true: honest — no peek, no assumed deck. Reads the opponent's public cards,
    /// infers a belief over the curated archetypes
    /// (<see cref="Majik.Bot.OpponentModel.ArchetypeInferencer"/>), allocates the
    /// determinized worlds across that belief
    /// (<see cref="Majik.Bot.OpponentModel.WorldAllocator"/>), and runs
    /// belief-driven determinized search
    /// (<see cref="Majik.Bot.Search.DeterminizedSearch.RunBelief"/>). Plays
    /// <see cref="AsymmetricBotDeck"/>; must INFER <see cref="AsymmetricOppDeck"/>.</summary>
    internal static BotConfig Inference(int seed) => new BotConfig(
        AsymmetricBotDeck, Strategy: "mcts",
        RandomSeed: seed,
        MaxMctsIterations: MctsIterations,
        MaxMctsBudgetMs: MctsBudgetMs,
        PrioritySearchEnabled: true,
        OpponentArchetype: null,
        InferOpponentArchetype: true);

    /// <summary>Perfect-info (Prowess seat) = mcts, OpponentArchetype null, no
    /// inference: peeks at the real hidden zones when it clones the live state.</summary>
    internal static BotConfig PerfectInfoBot(int seed) => new BotConfig(
        AsymmetricBotDeck, Strategy: "mcts",
        RandomSeed: seed,
        MaxMctsIterations: MctsIterations,
        MaxMctsBudgetMs: MctsBudgetMs,
        PrioritySearchEnabled: true,
        OpponentArchetype: null,
        InferOpponentArchetype: false);

    /// <summary>Perfect-info (Burn opponent seat) = mcts, OpponentArchetype null,
    /// no inference → today's perfect-info MCTS (peeks). Plays
    /// <see cref="AsymmetricOppDeck"/>.</summary>
    internal static BotConfig PerfectInfoOpp(int seed) => new BotConfig(
        AsymmetricOppDeck, Strategy: "mcts",
        RandomSeed: seed,
        MaxMctsIterations: MctsIterations,
        MaxMctsBudgetMs: MctsBudgetMs,
        PrioritySearchEnabled: true,
        OpponentArchetype: null,
        InferOpponentArchetype: false);

    /// <summary>Heuristic Burn opponent.</summary>
    internal static BotConfig HeuristicOpp(int seed) => new BotConfig(
        AsymmetricOppDeck, Strategy: "heuristic", RandomSeed: seed);

    /// <summary>Heuristic Prowess seat — the ANCHOR's bot-under-test seat
    /// (<c>AnchorProbes.cs</c>): the same deck the MCTS bots play in this
    /// family, but with the heuristic strategy, so the matchup's intrinsic
    /// win-rate can be measured with the search bot out of the picture.</summary>
    internal static BotConfig HeuristicBot(int seed) => new BotConfig(
        AsymmetricBotDeck, Strategy: "heuristic", RandomSeed: seed);

    /// <summary>Known-archetype determinized (Prowess seat): honest (no peek) but
    /// TOLD the opponent is Burn (OpponentArchetype set, inference off) →
    /// single-decklist determinized search, no belief spread. Diagnostic: isolates
    /// the no-peek honesty cost from inference quality. If this beats heuristic
    /// clearly while Inference lags, the gap is inference quality
    /// (wrong-archetype worlds); if this ≈ Inference, the gap is just the price
    /// of not peeking.</summary>
    internal static BotConfig KnownDeterminized(int seed) => new BotConfig(
        AsymmetricBotDeck, Strategy: "mcts",
        RandomSeed: seed,
        MaxMctsIterations: MctsIterations,
        MaxMctsBudgetMs: MctsBudgetMs,
        PrioritySearchEnabled: true,
        OpponentArchetype: AsymmetricOppDeck,
        InferOpponentArchetype: false);

    // ── BotConfig factories — MIRROR (determinization) family ───────────────────

    /// <summary>Determinized = mcts with OpponentArchetype set (honest, resamples
    /// hidden zones from the known mirror decklist; no peek).</summary>
    internal static BotConfig MirrorDeterminized(int seed) => new BotConfig(
        MirrorArchetype, Strategy: "mcts",
        RandomSeed: seed,
        MaxMctsIterations: MctsIterations,
        MaxMctsBudgetMs: MctsBudgetMs,
        PrioritySearchEnabled: true,
        OpponentArchetype: MirrorArchetype);

    /// <summary>Perfect-info = mcts with OpponentArchetype null (peeks at the real
    /// hand when it clones the live state for search).</summary>
    internal static BotConfig MirrorPerfectInfo(int seed) => new BotConfig(
        MirrorArchetype, Strategy: "mcts",
        RandomSeed: seed,
        MaxMctsIterations: MctsIterations,
        MaxMctsBudgetMs: MctsBudgetMs,
        PrioritySearchEnabled: true,
        OpponentArchetype: null);

    /// <summary>Heuristic mirror seat.</summary>
    internal static BotConfig MirrorHeuristic(int seed) => new BotConfig(
        MirrorArchetype, Strategy: "heuristic", RandomSeed: seed);

    // ── BotConfig factories — ROLLOUT-DEPTH MATRIX cells ────────────────────────
    // Matrix variants of MirrorDeterminized / Inference: same honest search, but
    // wall-clock-BOUND at the live budget (MatrixBudgetMs = 1500 ms) with a
    // per-cell iteration cap + truncated RolloutDepth. The matrix's question:
    // does a cheaper rollout convert the SAME live budget into more useful
    // iterations? (See RolloutDepthMatrixProbes.cs for the cell grid.)

    /// <summary>
    /// Matrix-cell variant of <see cref="MirrorDeterminized"/> (honest,
    /// resamples from the known mirror decklist; no peek) at the given
    /// <paramref name="depth"/> + <paramref name="iterations"/>, wall-clock-bound
    /// at <see cref="MatrixBudgetMs"/>. <c>preserves: MirrorArchetype, Strategy,
    /// PrioritySearchEnabled, OpponentArchetype</c> from the live factory.
    /// </summary>
    internal static Func<int, BotConfig> MirrorDeterminizedAt(RolloutDepth depth, int iterations) =>
        seed => new BotConfig(
            MirrorArchetype, Strategy: "mcts",
            RandomSeed: seed,
            MaxMctsIterations: iterations,
            MaxMctsBudgetMs: MatrixBudgetMs,
            PrioritySearchEnabled: true,
            OpponentArchetype: MirrorArchetype,
            RolloutDepth: depth.ToString());

    /// <summary>
    /// Matrix-cell variant of <see cref="Inference"/> (honest — no peek, no
    /// assumed deck; infers the opponent's archetype from public cards) at the
    /// given <paramref name="depth"/> + <paramref name="iterations"/>,
    /// wall-clock-bound at <see cref="MatrixBudgetMs"/>. <c>preserves:
    /// AsymmetricBotDeck, Strategy, PrioritySearchEnabled, OpponentArchetype,
    /// InferOpponentArchetype</c> from the live factory.
    /// </summary>
    internal static Func<int, BotConfig> InferenceAt(RolloutDepth depth, int iterations) =>
        seed => new BotConfig(
            AsymmetricBotDeck, Strategy: "mcts",
            RandomSeed: seed,
            MaxMctsIterations: iterations,
            MaxMctsBudgetMs: MatrixBudgetMs,
            PrioritySearchEnabled: true,
            OpponentArchetype: null,
            InferOpponentArchetype: true,
            RolloutDepth: depth.ToString());

    // ── BotConfig factories — TREE-STATE-REUSE cells ────────────────────────────
    // Reuse variants of MirrorDeterminized / Inference: same honest search,
    // wall-clock-bound at the live budget (MatrixBudgetMs = 1500 ms) with
    // TreeStateReuse ON. Two iteration caps per head: the live 150 (latency
    // read — reuse finishes the same iterations FASTER) and the measured
    // 1-core capacity (strength read — reuse converts the SAME live budget
    // into ~5× the iterations; see the decisionReuse profiler cells).

    /// <summary>
    /// Reuse-cell variant of <see cref="MirrorDeterminized"/> (honest,
    /// resamples from the known mirror decklist; no peek) with
    /// <c>TreeStateReuse</c> ON at the given <paramref name="iterations"/>,
    /// wall-clock-bound at <see cref="MatrixBudgetMs"/>. <c>preserves:
    /// MirrorArchetype, Strategy, PrioritySearchEnabled, OpponentArchetype</c>
    /// from the live factory.
    /// </summary>
    internal static Func<int, BotConfig> MirrorDeterminizedReuseAt(int iterations) =>
        seed => new BotConfig(
            MirrorArchetype, Strategy: "mcts",
            RandomSeed: seed,
            MaxMctsIterations: iterations,
            MaxMctsBudgetMs: MatrixBudgetMs,
            PrioritySearchEnabled: true,
            OpponentArchetype: MirrorArchetype,
            TreeStateReuse: true);

    /// <summary>
    /// Reuse-cell variant of <see cref="Inference"/> (honest — no peek, no
    /// assumed deck; infers the opponent's archetype from public cards) with
    /// <c>TreeStateReuse</c> ON at the given <paramref name="iterations"/>,
    /// wall-clock-bound at <see cref="MatrixBudgetMs"/>. <c>preserves:
    /// AsymmetricBotDeck, Strategy, PrioritySearchEnabled, OpponentArchetype,
    /// InferOpponentArchetype</c> from the live factory.
    /// </summary>
    internal static Func<int, BotConfig> InferenceReuseAt(int iterations) =>
        seed => new BotConfig(
            AsymmetricBotDeck, Strategy: "mcts",
            RandomSeed: seed,
            MaxMctsIterations: iterations,
            MaxMctsBudgetMs: MatrixBudgetMs,
            PrioritySearchEnabled: true,
            OpponentArchetype: null,
            InferOpponentArchetype: true,
            TreeStateReuse: true);

    // ── BotConfig factories — WORLD-SPLIT (K-tuning) cells ──────────────────────
    // World-split variants of MirrorDeterminizedReuseAt: the LIVE production
    // shape (reuse ON, wall-clock-bound at MatrixBudgetMs = 1500 ms, cap 800)
    // with an EXPLICIT determinized world split. The cells' question: now that
    // reuse makes iterations cheap, do MORE worlds beat more iterations per
    // world at the SAME live budget? (See WorldSplitProbes.cs for the cells.)

    /// <summary>
    /// World-split cell variant of <see cref="MirrorDeterminizedReuseAt"/>
    /// (honest, resamples from the known mirror decklist; no peek; reuse ON) at
    /// an EXPLICIT (<paramref name="maxWorlds"/>, <paramref name="perWorldMs"/>)
    /// split, wall-clock-bound at <see cref="MatrixBudgetMs"/> with the given
    /// <paramref name="iterations"/> cap. K = clamp(round(<see cref="MatrixBudgetMs"/>
    /// / perWorldMs), 1, maxWorlds); the per-world iteration cap scales by the
    /// same perWorld/total fraction. <c>preserves: MirrorArchetype, Strategy,
    /// PrioritySearchEnabled, OpponentArchetype, TreeStateReuse</c> from the
    /// reuse-cell factory.
    /// </summary>
    internal static Func<int, BotConfig> MirrorDeterminizedWorldSplitAt(
        int iterations, int maxWorlds, int perWorldMs) =>
        seed => new BotConfig(
            MirrorArchetype, Strategy: "mcts",
            RandomSeed: seed,
            MaxMctsIterations: iterations,
            MaxMctsBudgetMs: MatrixBudgetMs,
            PrioritySearchEnabled: true,
            OpponentArchetype: MirrorArchetype,
            TreeStateReuse: true,
            MaxWorlds: maxWorlds,
            PerWorldBudgetMs: perWorldMs);

    // ── Head-to-head runner ─────────────────────────────────────────────────────

    /// <summary>
    /// Plays <see cref="Games"/> games of seat-A-strategy (playing
    /// <paramref name="seatADeck"/>) vs seat-B-strategy (playing
    /// <paramref name="seatBDeck"/>), alternating which physical seat (Alice/Bob)
    /// hosts strategy A across games to cancel play/draw bias. Each seat's DECK
    /// travels with its strategy, so when seat A swaps to Bob its deck swaps too
    /// (for the mirror family both decks are the same archetype and this is a
    /// no-op). Returns (aWins, decided, draws, inconclusive). Each game uses a
    /// distinct fixed seed <c>seedBlock + i</c> for reproducible variety.
    /// Per-game and <c>[STRENGTH]</c> summary lines stream to
    /// <see cref="ProbeProgress"/> (carrying <paramref name="label"/>) as well as
    /// the xUnit output sink.
    /// </summary>
    internal static async Task<(int AWins, int Decided, int Draws, int Inconclusive)> RunHeadToHead(
        ITestOutputHelper output,
        string label,
        Func<int, BotConfig> seatA,
        string seatADeck,
        Func<int, BotConfig> seatB,
        string seatBDeck,
        int seedBlock)
    {
        int aWins = 0, bWins = 0, draws = 0, inconclusive = 0;

        for (int i = 0; i < Games; i++)
        {
            // Alternate which physical seat (Alice) hosts strategy A so neither the
            // play nor the draw is systematically assigned to one strategy.
            bool aIsAlice = i % 2 == 0;
            int seed = seedBlock + i;

            var outcome = await PlayOneGame(
                label: label, aIsAlice: aIsAlice, seed: seed, gameIndex: i,
                seatAConfig: seatA, seatADeck: seatADeck,
                seatBConfig: seatB, seatBDeck: seatBDeck);

            switch (outcome)
            {
                case SeatAWinner.SeatA:        aWins++;        break;
                case SeatAWinner.SeatB:        bWins++;        break;
                case SeatAWinner.Draw:         draws++;         break;
                case SeatAWinner.Inconclusive: inconclusive++;  break;
            }

            var gameLine =
                $"  [{label}] game {i,2}: seed={seed} A={(aIsAlice ? "Alice" : "Bob")} " +
                $"result={outcome}  cumulative: A {aWins} B {bWins} draw {draws} inconclusive {inconclusive}";
            output.WriteLine(gameLine);
            ProbeProgress.Log(gameLine);   // streams live; xUnit buffers output until the test ends
        }

        int decided = aWins + bWins;
        double winRate = decided > 0 ? (double)aWins / decided : 0.0;
        var summaryLine =
            $"[STRENGTH] [{label}] A {aWins}/{decided} decided " +
            $"({Games} played, {draws} draws, {inconclusive} inconclusive) win-rate={winRate:P1}";
        output.WriteLine(summaryLine);
        ProbeProgress.Log(summaryLine);

        return (aWins, decided, draws, inconclusive);
    }

    // ── Per-probe summary + liveness assertions ─────────────────────────────────

    /// <summary>Win-rate over decided games (0 when nothing was decided).</summary>
    internal static double WinRate((int AWins, int Decided, int Draws, int Inconclusive) r) =>
        r.Decided > 0 ? (double)r.AWins / r.Decided : 0.0;

    /// <summary>
    /// Emits the single grep-able per-probe summary line for the controller
    /// (tagged <c>[INFER]</c> or <c>[DET]</c> by family) to both the xUnit sink
    /// and <see cref="ProbeProgress"/>. The rollout-depth MATRIX cells pass
    /// their per-cell <paramref name="iterations"/> / <paramref name="budgetMs"/>
    /// (defaults = the iteration-bound family consts).
    /// </summary>
    internal static void LogSummary(
        ITestOutputHelper output,
        string tag,
        string label,
        string decks,
        (int AWins, int Decided, int Draws, int Inconclusive) r,
        int iterations = MctsIterations,
        int budgetMs = MctsBudgetMs)
    {
        double rate = WinRate(r);
        var line =
            $"{tag} head={label} {decks} N={Games}  " +
            $"{label}={rate:P0} ({r.AWins}/{r.Decided})  " +
            $"draws={r.Draws} inconclusive={r.Inconclusive}  " +
            $"iter={iterations} budgetMs={budgetMs} maxTurns={MaxTurns} prioritySearch=true";
        output.WriteLine(line);
        ProbeProgress.Log(line);
    }

    /// <summary>
    /// Liveness-only assertions (NOT a win% threshold): the head-to-head must
    /// have decided at least one game so its logged win-rate is meaningful, and
    /// the rate must be finite in [0,1]. The controller makes the strength
    /// judgment from the logged summary lines.
    /// </summary>
    internal static void AssertLiveness(
        string label,
        (int AWins, int Decided, int Draws, int Inconclusive) r)
    {
        r.Decided.Should().BeGreaterThan(0,
            $"{label} must decide at least one game for its win-rate to be meaningful");
        WinRate(r).Should().BeInRange(0.0, 1.0);
    }

    // ── Single game ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Run one game of seat-A-strategy (deck <paramref name="seatADeck"/>) vs
    /// seat-B-strategy (deck <paramref name="seatBDeck"/>) and return which
    /// strategy won (or Draw at the turn cap / Inconclusive on an engine crash).
    /// Each seat's DECK is loaded via <c>DeckLoader.LoadReal(thatSeatsArchetype, Repo)</c>
    /// so the board develops normally (see <see cref="SearchVsHeuristicTests"/>
    /// for why the vanilla loader causes land-starved draws). The deck follows
    /// the strategy when seat A hosts Bob. A single crashed game is counted
    /// Inconclusive and logged so it cannot abort the whole run.
    /// </summary>
    private static async Task<SeatAWinner> PlayOneGame(
        string label,
        bool aIsAlice,
        int seed,
        int gameIndex,
        Func<int, BotConfig> seatAConfig,
        string seatADeck,
        Func<int, BotConfig> seatBConfig,
        string seatBDeck)
    {
        string aliceName = aIsAlice ? "A" : "B";
        string bobName   = aIsAlice ? "B" : "A";

        // The deck travels with the strategy: Alice gets seat-A's deck when A is
        // Alice, else seat-B's deck. This keeps asymmetric matchups correct
        // regardless of which physical seat hosts the bot under test.
        string aliceDeckName = aIsAlice ? seatADeck : seatBDeck;
        string bobDeckName   = aIsAlice ? seatBDeck : seatADeck;

        var facade = GameFacade.Create(
            aliceName: aliceName,
            bobName:   bobName,
            aliceDeck: DeckLoader.LoadReal(aliceDeckName, Repo),
            bobDeck:   DeckLoader.LoadReal(bobDeckName, Repo),
            cardRepo:  Repo);

        // Distinct per-seat seeds (B offset by +500) so the two bots' tie-break
        // RNGs differ, matching the existing harness convention.
        var aCfg = seatAConfig(seed);
        var bCfg = seatBConfig(seed + 500);

        if (aIsAlice)
        {
            facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, aCfg));
            facade.ReplaceBobAgent(  new BotPlayerAgent(facade.Bob,   bCfg));
        }
        else
        {
            facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, bCfg));
            facade.ReplaceBobAgent(  new BotPlayerAgent(facade.Bob,   aCfg));
        }

        // Per-game cap; the determinized/inference bots' K-world loop can be
        // slower than the single-tree perfect-info bot. Generous relative to the
        // iteration-bound search so parallel CPU contention cannot trip it.
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(6));

        try
        {
            await facade.StartFullGameAsync(
                maxTurns: MaxTurns,
                ct: cts.Token,
                rng: new GameRandom(seed));

            var result = await facade.FullGameTask!;

            if (result.Winner == null)
                return SeatAWinner.Draw;

            // Strategy A sits on Alice when aIsAlice, else on Bob.
            bool aWon = aIsAlice
                ? ReferenceEquals(result.Winner, facade.Alice)
                : ReferenceEquals(result.Winner, facade.Bob);

            return aWon ? SeatAWinner.SeatA : SeatAWinner.SeatB;
        }
        catch (Exception ex)
        {
            // One crash must not abort the whole probe.
            // Logged via Console + ProbeProgress because this helper is static
            // (no ITestOutputHelper); xUnit forwards Console to the test runner
            // stdout the controller reads.
            Console.WriteLine(
                $"  [{label}] game {gameIndex,2}: INCONCLUSIVE — {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"    stack: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
            return SeatAWinner.Inconclusive;
        }
    }
}
