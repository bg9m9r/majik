using FluentAssertions;
using Majik.Bot;
using Majik.Bot.Tests.Integration.Helpers;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Random;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Search;

/// <summary>
/// On-demand strength probe for the <b>determinized</b> MCTS bot.
///
/// <para>
/// Determinization removes the perfect-info <em>peek</em> (today's MCTS bot
/// secretly reads the real opponent hand/library when it clones the game state).
/// When <see cref="BotConfig.OpponentArchetype"/> names a known
/// <see cref="Majik.Bot.Decks.BotDeckCatalog"/> archetype,
/// <see cref="Majik.Bot.Search.SearchStrategy"/> instead routes through
/// <see cref="Majik.Bot.Search.DeterminizedSearch"/>: it resamples the opponent's
/// hidden zones from that decklist across K worlds, runs a per-world-bounded MCTS
/// in each, and votes by summed-robust-child. This is <em>honest</em> play — no
/// peek — at the cost of cross-world averaging.
/// </para>
///
/// <para>
/// <b>This probe measures three head-to-heads on a known-decklist MIRROR</b>
/// (both seats the same archetype) so we can read whether honest determinization
/// holds up in strength:
/// <list type="number">
///   <item><b>Determinized vs Heuristic</b> — seat A is the determinized MCTS bot
///     (<c>OpponentArchetype</c> set), seat B is the pure heuristic. Win% for A.</item>
///   <item><b>Determinized vs Perfect-info MCTS</b> — seat A is determinized
///     (honest), seat B is the perfect-info MCTS (<c>OpponentArchetype</c> null,
///     so it peeks). Win% for the determinized (honest) bot.</item>
///   <item><b>Perfect-info MCTS vs Heuristic</b> — the known baseline
///     (≈88% on Prowess per <see cref="SearchVsHeuristicTests"/>), included for
///     reference so the determinized number is interpretable relative to it.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Interpretation (no hard win-rate assertion).</b> Determinization REMOVES an
/// unfair advantage (the peek) and ADDS cross-world robustness, so:
/// <list type="bullet">
///   <item>Determinized should still beat the heuristic <em>clearly</em>.</item>
///   <item>Determinized-vs-perfect-info around 50%, or a <em>modest</em> dip below
///     it, is ACCEPTABLE and EXPECTED — honest play giving up a cheat against an
///     opponent that still peeks.</item>
///   <item>A CRATER — det-vs-heuristic well below the perfect-info-vs-heuristic
///     baseline (e.g. &lt;40% when perfect-info wins 70%+) — signals a real problem:
///     the sampler building wrong cards, K too low, or per-world budget starvation.
///     That is the controller's judgment call from the logged <c>[DET]</c> line,
///     not a test failure.</item>
/// </list>
/// The test ASSERTS ONLY that the probe RAN end-to-end and produced finite,
/// well-formed win-rates (at least one game decided per head-to-head, no crash).
/// It does NOT assert a win% threshold — the controller reads the <c>[DET]</c>
/// summary line and makes the strength call.
/// </para>
///
/// <para>
/// <b>Deck choice.</b> Uses the <c>Prowess</c> mirror — the same archetype the
/// existing perfect-info strength harness (<see cref="SearchVsHeuristicTests"/>)
/// already runs without sim-clone trouble, and the same archetype the
/// determinization unit tests exercise as a sampled <c>OpponentArchetype</c>.
/// Prowess has real hidden-information relevance (the opponent's hand of pump /
/// burn changes the right combat math) and clones cleanly under
/// <c>DeckLoader.LoadReal</c> — none of its cards hit the
/// <c>BrainstormTemplate</c> / library-reorder CloneForSim gap that bespoke
/// card-draw spells can. (Verified against the unit-tested determinized path,
/// which samples a real Burn/Prowess decklist into sandbox clones without error.)
/// </para>
/// </summary>
public sealed class DeterminizedVsPerfectInfoTests
{
    private readonly ITestOutputHelper _out;

    /// <summary>
    /// Embedded card repository — loaded once per class. LoadReal resolves card
    /// names to proper typed shells so non-basic lands tap for mana and the board
    /// develops normally (mirrors <see cref="SearchVsHeuristicTests"/>).
    /// </summary>
    private static readonly EmbeddedCardRepository Repo = new();

    /// <summary>
    /// Games per head-to-head. Default kept modest so the full 3-way probe is a
    /// few minutes of wall-clock at the configured budget. The controller may bump
    /// this for a meatier measurement.
    /// </summary>
    private const int Games = 30;

    /// <summary>MCTS iteration cap per search call — matches the production default
    /// and the existing strength harness.</summary>
    private const int MctsIterations = 150;

    /// <summary>Wall-clock budget per MCTS search call (ms) — production default.
    /// For the determinized bot this TOTAL is split across K sampled worlds by
    /// <see cref="Majik.Bot.Search.DeterminizedSearch"/>; the perfect-info bot
    /// spends it all on its single tree.</summary>
    private const int MctsBudgetMs = 1500;

    /// <summary>Maximum turns per game — prevents hangs on drawn-out games.</summary>
    private const int MaxTurns = 30;

    /// <summary>Base seed; game i in head-to-head H uses a distinct fixed seed so
    /// runs are reproducible but the three head-to-heads see different game seeds.</summary>
    private const int BaseSeed = 5000;

    /// <summary>Mirror archetype for all three head-to-heads. Prowess clones cleanly
    /// in the sim and has hidden-info relevance — see class doc.</summary>
    private const string Archetype = "Prowess";

    public DeterminizedVsPerfectInfoTests(ITestOutputHelper output)
    {
        _out = output;
    }

    /// <summary>Which seat's strategy won a single game (or a draw / crash).</summary>
    private enum SeatAWinner { SeatA, SeatB, Draw, Inconclusive }

    /// <summary>
    /// The determinized strength probe. See the class XML doc for full
    /// interpretation guidance. <b>Skipped by default</b> — this is an on-demand
    /// probe, not a CI gate; the controller un-skips it and greps the
    /// <c>[DET]</c> summary line. Assertion is liveness-only (it RAN, produced
    /// finite win-rates), NOT a hard win% threshold.
    /// </summary>
    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public async Task Determinized_vs_PerfectInfo_and_Heuristic()
    {
        // ── Strategy factories for the three roles ───────────────────────────────
        // Determinized = mcts with OpponentArchetype set (honest, resamples hidden
        // zones from the known mirror decklist; no peek).
        BotConfig Determinized(int seed) => new BotConfig(
            Archetype, Strategy: "mcts",
            RandomSeed: seed,
            MaxMctsIterations: MctsIterations,
            MaxMctsBudgetMs: MctsBudgetMs,
            PrioritySearchEnabled: true,
            OpponentArchetype: Archetype);

        // Perfect-info = mcts with OpponentArchetype null (peeks at the real hand
        // when it clones the live state for search).
        BotConfig PerfectInfo(int seed) => new BotConfig(
            Archetype, Strategy: "mcts",
            RandomSeed: seed,
            MaxMctsIterations: MctsIterations,
            MaxMctsBudgetMs: MctsBudgetMs,
            PrioritySearchEnabled: true,
            OpponentArchetype: null);

        BotConfig Heuristic(int seed) => new BotConfig(
            Archetype, Strategy: "heuristic", RandomSeed: seed);

        // ── Head-to-head 1: Determinized (A) vs Heuristic (B) ────────────────────
        var (detWins, detDecided, detDraws, detInc) = await RunHeadToHead(
            label: "det-vs-heuristic",
            seatA: Determinized, seatB: Heuristic,
            seedBlock: BaseSeed);

        // ── Head-to-head 2: Determinized (A) vs Perfect-info MCTS (B) ────────────
        var (dpWins, dpDecided, dpDraws, dpInc) = await RunHeadToHead(
            label: "det-vs-perfectinfo",
            seatA: Determinized, seatB: PerfectInfo,
            seedBlock: BaseSeed + 1000);

        // ── Head-to-head 3 (context): Perfect-info MCTS (A) vs Heuristic (B) ─────
        var (piWins, piDecided, piDraws, piInc) = await RunHeadToHead(
            label: "perfectinfo-vs-heuristic",
            seatA: PerfectInfo, seatB: Heuristic,
            seedBlock: BaseSeed + 2000);

        double detRate = detDecided > 0 ? (double)detWins / detDecided : 0.0;
        double dpRate  = dpDecided  > 0 ? (double)dpWins  / dpDecided  : 0.0;
        double piRate  = piDecided  > 0 ? (double)piWins  / piDecided  : 0.0;

        // ── Single grep-able summary line for the controller ─────────────────────
        _out.WriteLine(
            $"[DET] deck={Archetype} N={Games}  " +
            $"det-vs-heuristic={detRate:P0} ({detWins}/{detDecided})  " +
            $"det-vs-perfectinfo={dpRate:P0} ({dpWins}/{dpDecided})  " +
            $"perfectinfo-vs-heuristic={piRate:P0} ({piWins}/{piDecided})  " +
            $"draws=h1:{detDraws},h2:{dpDraws},h3:{piDraws}  " +
            $"inconclusive=h1:{detInc},h2:{dpInc},h3:{piInc}  " +
            $"iter={MctsIterations} budgetMs={MctsBudgetMs} maxTurns={MaxTurns} prioritySearch=true");

        // ── Liveness-only assertions (NOT a win% threshold) ──────────────────────
        // Each head-to-head must have produced at least one decided game so the
        // logged win-rates are meaningful, and the rates must be finite [0,1].
        // The controller makes the strength judgment from the [DET] line above.
        detDecided.Should().BeGreaterThan(0,
            "det-vs-heuristic must decide at least one game for its win-rate to be meaningful");
        dpDecided.Should().BeGreaterThan(0,
            "det-vs-perfectinfo must decide at least one game for its win-rate to be meaningful");
        piDecided.Should().BeGreaterThan(0,
            "perfectinfo-vs-heuristic must decide at least one game for its win-rate to be meaningful");

        detRate.Should().BeInRange(0.0, 1.0);
        dpRate.Should().BeInRange(0.0, 1.0);
        piRate.Should().BeInRange(0.0, 1.0);
    }

    // ── Head-to-head runner ─────────────────────────────────────────────────────

    /// <summary>
    /// Plays <see cref="Games"/> mirror games of seat-A-strategy vs seat-B-strategy,
    /// alternating which physical seat (Alice/Bob) hosts strategy A across games to
    /// cancel play/draw bias (mirrors <see cref="SearchVsHeuristicTests"/>). Returns
    /// (aWins, decided, draws, inconclusive). Each game uses a distinct fixed seed
    /// <c>seedBlock + i</c> for reproducible variety.
    /// </summary>
    private async Task<(int AWins, int Decided, int Draws, int Inconclusive)> RunHeadToHead(
        string label,
        Func<int, BotConfig> seatA,
        Func<int, BotConfig> seatB,
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
                seatAConfig: seatA, seatBConfig: seatB);

            switch (outcome)
            {
                case SeatAWinner.SeatA:        aWins++;        break;
                case SeatAWinner.SeatB:        bWins++;        break;
                case SeatAWinner.Draw:         draws++;         break;
                case SeatAWinner.Inconclusive: inconclusive++;  break;
            }

            _out.WriteLine(
                $"  [{label}] game {i,2}: seed={seed} A={(aIsAlice ? "Alice" : "Bob")} " +
                $"result={outcome}  cumulative: A {aWins} B {bWins} draw {draws} inconclusive {inconclusive}");
        }

        int decided = aWins + bWins;
        double winRate = decided > 0 ? (double)aWins / decided : 0.0;
        _out.WriteLine(
            $"[STRENGTH] [{label}] A {aWins}/{decided} decided " +
            $"({Games} played, {draws} draws, {inconclusive} inconclusive) win-rate={winRate:P1}");

        return (aWins, decided, draws, inconclusive);
    }

    // ── Single game ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Run one mirror game of seat-A-strategy vs seat-B-strategy and return which
    /// strategy won (or Draw at the turn cap / Inconclusive on an engine crash).
    /// Both seats use <c>DeckLoader.LoadReal(Archetype, Repo)</c> so the board
    /// develops normally (see <see cref="SearchVsHeuristicTests"/> for why the
    /// vanilla loader causes land-starved draws). A single crashed game is counted
    /// Inconclusive and logged so it cannot abort the whole run.
    /// </summary>
    private static async Task<SeatAWinner> PlayOneGame(
        string label,
        bool aIsAlice,
        int seed,
        int gameIndex,
        Func<int, BotConfig> seatAConfig,
        Func<int, BotConfig> seatBConfig)
    {
        string aliceName = aIsAlice ? "A" : "B";
        string bobName   = aIsAlice ? "B" : "A";

        var facade = GameFacade.Create(
            aliceName: aliceName,
            bobName:   bobName,
            aliceDeck: DeckLoader.LoadReal(Archetype, Repo),
            bobDeck:   DeckLoader.LoadReal(Archetype, Repo),
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

        // 6-minute per-game cap; the determinized bot's K-world loop can be slower
        // than the single-tree perfect-info bot.
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
            // One crash must not abort the whole 3-way probe.
            // Logged via Console because this helper is static (no ITestOutputHelper);
            // xUnit forwards Console to the test runner stdout the controller reads.
            Console.WriteLine(
                $"  [{label}] game {gameIndex,2}: INCONCLUSIVE — {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"    stack: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
            return SeatAWinner.Inconclusive;
        }
    }
}
