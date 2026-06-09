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
/// On-demand strength probe for the <b>honest-vs-human inference</b> bot on an
/// <b>ASYMMETRIC</b> matchup.
///
/// <para>
/// Determinization (<see cref="DeterminizedVsPerfectInfoTests"/>) removes the
/// perfect-info <em>peek</em> when the opponent's archetype is KNOWN and named
/// via <see cref="BotConfig.OpponentArchetype"/>. <b>Inference</b> goes one step
/// further: with <see cref="BotConfig.InferOpponentArchetype"/><c> = true</c>
/// (and <c>OpponentArchetype</c> null, <c>Strategy = "mcts"</c>) the bot does NOT
/// know the opponent's deck. It reads the opponent's PUBLIC cards from the live
/// <see cref="Majik.Core.Game.GameContext"/>, infers a normalized belief over the
/// curated archetypes (<see cref="Majik.Bot.OpponentModel.ArchetypeInferencer"/>),
/// allocates the determinized worlds across that belief
/// (<see cref="Majik.Bot.OpponentModel.WorldAllocator"/>), and runs belief-driven
/// determinized search (<see cref="Majik.Bot.Search.DeterminizedSearch.RunBelief"/>).
/// This is <em>honest</em> play — no peek — and, crucially, no assumption: the
/// opponent's deck is inferred, not handed to the bot.
/// </para>
///
/// <para>
/// <b>Why ASYMMETRIC.</b> The determinization probe runs a MIRROR (both seats the
/// same archetype), so even a wrong/over-confident sampler tends to land near the
/// truth — the opponent really IS the archetype the bot would guess. This probe is
/// the harder, more honest test: the bot under test plays one archetype
/// (<c>Prowess</c>) while the opponent plays a DIFFERENT one (<c>Burn</c>) that the
/// bot must correctly INFER from public cards. A broken inferencer (or wrong-deck
/// sampling) shows up here as a strength crater that the mirror would mask.
/// </para>
///
/// <para>
/// <b>This probe measures three head-to-heads:</b>
/// <list type="number">
///   <item><b>Inference-bot vs Heuristic</b> — the bot under test plays Prowess
///     with <c>InferOpponentArchetype = true</c> (honest, infers Burn); the
///     opponent plays Burn with the pure heuristic. Win% for the inference bot.
///     This is the headline: honest inference vs a baseline opponent.</item>
///   <item><b>Inference-bot vs Perfect-info MCTS</b> — the bot under test plays
///     Prowess (infers Burn, honest); the opponent plays Burn with perfect-info
///     MCTS (<c>OpponentArchetype</c> null, <c>InferOpponentArchetype</c> false →
///     it PEEKS at the real hidden zones when it clones the state). Win% for the
///     honest inference bot against an opponent that cheats.</item>
///   <item><b>Perfect-info vs Heuristic</b> (baseline) — the bot under test plays
///     Prowess with perfect-info MCTS (peeks, no inference); the opponent plays
///     Burn with the heuristic. This is the ceiling the inference number is read
///     against: how strong the same search is on this matchup when it cheats.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Interpretation (no hard win-rate assertion).</b> Inference REMOVES an unfair
/// advantage (the peek) AND must correctly identify a DIFFERENT opponent deck, so:
/// <list type="bullet">
///   <item>Inference should still beat the heuristic <em>clearly</em>. The
///     determinization mirror baseline was ≈75–79% det-vs-heuristic; the asymmetric
///     inference number is expected in the same neighbourhood (a modest honest dip
///     is fine — the bot pays for honesty + the inference step).</item>
///   <item>Inference-vs-perfect-info around 50%, or a <em>modest</em> dip below it,
///     is ACCEPTABLE and EXPECTED — honest play giving up a cheat against an
///     opponent that still peeks.</item>
///   <item>A CRATER — inference-vs-heuristic well below the perfect-info-vs-heuristic
///     baseline (e.g. &lt;40% when perfect-info wins 70%+) — signals a real problem:
///     bad inference (it never identifies Burn), wrong-deck sampling, K too low, or
///     per-world budget starvation. That is the controller's judgment call from the
///     logged <c>[INFER]</c> line, not a test failure.</item>
/// </list>
/// The test ASSERTS ONLY that the probe RAN end-to-end and produced finite,
/// well-formed win-rates (at least one game decided per head-to-head, no crash).
/// It does NOT assert a win% threshold — the controller reads the <c>[INFER]</c>
/// summary line and makes the strength call.
/// </para>
///
/// <para>
/// <b>Deck choice — Prowess (bot) vs Burn (opponent).</b> Both are archetypes the
/// determinization probe and the perfect-info strength harness
/// (<see cref="SearchVsHeuristicTests"/>) already exercise without sim-clone
/// trouble, and both are in the inferencer's candidate set
/// (<see cref="Majik.Bot.OpponentModel.ArchetypeInferencer"/> over
/// <c>BotDeckCatalog.Archetypes</c>) and the metagame prior — so the bot CAN
/// converge on Burn from public cards. Both clone cleanly under
/// <c>DeckLoader.LoadReal</c>: neither hits the <c>BrainstormTemplate</c> /
/// library-reorder <c>CloneForSim</c> gap that bespoke card-draw spells can. The
/// pair is also genuinely asymmetric with hidden-information relevance — the
/// opponent's Burn hand (reach / direct damage) changes Prowess's correct
/// race / combat math, so inferring "Burn" rather than "mirror" matters.
/// </para>
/// </summary>
public sealed class InferenceVsPerfectInfoTests
{
    private readonly ITestOutputHelper _out;

    /// <summary>
    /// Embedded card repository — loaded once per class. LoadReal resolves card
    /// names to proper typed shells so non-basic lands tap for mana and the board
    /// develops normally (mirrors <see cref="DeterminizedVsPerfectInfoTests"/>).
    /// </summary>
    private static readonly EmbeddedCardRepository Repo = new();

    /// <summary>
    /// Games per head-to-head. Default kept modest so the full 3-way probe is a
    /// few minutes of wall-clock at the configured budget. The controller may bump
    /// this for a meatier measurement.
    /// </summary>
    private const int Games = 30;

    /// <summary>MCTS iteration cap per search call — matches the production default
    /// and the determinization probe.</summary>
    private const int MctsIterations = 150;

    /// <summary>Wall-clock budget per MCTS search call (ms) — production default.
    /// For the inference bot this TOTAL is split across the belief-allocated worlds
    /// by <see cref="Majik.Bot.Search.DeterminizedSearch"/>; the perfect-info bot
    /// spends it all on its single tree.</summary>
    private const int MctsBudgetMs = 1500;

    /// <summary>Maximum turns per game — prevents hangs on drawn-out games.</summary>
    private const int MaxTurns = 30;

    /// <summary>Base seed; game i in head-to-head H uses a distinct fixed seed so
    /// runs are reproducible but the three head-to-heads see different game seeds.</summary>
    private const int BaseSeed = 7000;

    /// <summary>The archetype the BOT UNDER TEST plays. It knows only its OWN deck;
    /// it must INFER the opponent's. Clones cleanly in the sim — see class doc.</summary>
    private const string BotDeck = "Prowess";

    /// <summary>The archetype the OPPONENT plays — DIFFERENT from <see cref="BotDeck"/>.
    /// The inference bot must converge on this from the opponent's public cards.
    /// Clones cleanly in the sim — see class doc.</summary>
    private const string OppDeck = "Burn";

    public InferenceVsPerfectInfoTests(ITestOutputHelper output)
    {
        _out = output;
    }

    /// <summary>Which seat's strategy won a single game (or a draw / crash).</summary>
    private enum SeatAWinner { SeatA, SeatB, Draw, Inconclusive }

    /// <summary>
    /// The honest-vs-human inference strength probe (ASYMMETRIC). See the class XML
    /// doc for full interpretation guidance. <b>Skipped by default</b> — this is an
    /// on-demand probe, not a CI gate; the controller un-skips it and greps the
    /// <c>[INFER]</c> summary line. Assertion is liveness-only (it RAN, produced
    /// finite win-rates), NOT a hard win% threshold.
    /// </summary>
    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public async Task Inference_vs_PerfectInfo_and_Heuristic()
    {
        // ── Strategy factories for the three roles ───────────────────────────────
        // The "bot under test" seat plays BotDeck (Prowess). The "opponent" seat
        // plays OppDeck (Burn). Each factory takes (seed) and is paired with the
        // correct DECK by RunHeadToHead — see the (config, deck) tuples below.

        // Inference = mcts, OpponentArchetype null, InferOpponentArchetype true:
        // honest — no peek, no assumed deck. Reads the opponent's public cards and
        // infers a belief over the curated archetypes, then runs belief-driven
        // determinized search. Plays BotDeck; must INFER OppDeck.
        BotConfig Inference(int seed) => new BotConfig(
            BotDeck, Strategy: "mcts",
            RandomSeed: seed,
            MaxMctsIterations: MctsIterations,
            MaxMctsBudgetMs: MctsBudgetMs,
            PrioritySearchEnabled: true,
            OpponentArchetype: null,
            InferOpponentArchetype: true);

        // Perfect-info (Prowess seat) = mcts, OpponentArchetype null, no inference:
        // peeks at the real hidden zones when it clones the live state for search.
        BotConfig PerfectInfoBot(int seed) => new BotConfig(
            BotDeck, Strategy: "mcts",
            RandomSeed: seed,
            MaxMctsIterations: MctsIterations,
            MaxMctsBudgetMs: MctsBudgetMs,
            PrioritySearchEnabled: true,
            OpponentArchetype: null,
            InferOpponentArchetype: false);

        // Perfect-info (Burn opponent seat) = mcts, OpponentArchetype null, no
        // inference → today's perfect-info MCTS (peeks). Plays OppDeck.
        BotConfig PerfectInfoOpp(int seed) => new BotConfig(
            OppDeck, Strategy: "mcts",
            RandomSeed: seed,
            MaxMctsIterations: MctsIterations,
            MaxMctsBudgetMs: MctsBudgetMs,
            PrioritySearchEnabled: true,
            OpponentArchetype: null,
            InferOpponentArchetype: false);

        // Heuristic Burn opponent.
        BotConfig HeuristicOpp(int seed) => new BotConfig(
            OppDeck, Strategy: "heuristic", RandomSeed: seed);

        // ── Head-to-head 1: Inference Prowess (A) vs Heuristic Burn (B) ──────────
        var (infWins, infDecided, infDraws, infInc) = await RunHeadToHead(
            label: "infer-vs-heuristic",
            seatA: Inference, seatADeck: BotDeck,
            seatB: HeuristicOpp, seatBDeck: OppDeck,
            seedBlock: BaseSeed);

        // ── Head-to-head 2: Inference Prowess (A) vs Perfect-info Burn (B) ───────
        var (ipWins, ipDecided, ipDraws, ipInc) = await RunHeadToHead(
            label: "infer-vs-perfectinfo",
            seatA: Inference, seatADeck: BotDeck,
            seatB: PerfectInfoOpp, seatBDeck: OppDeck,
            seedBlock: BaseSeed + 1000);

        // ── Head-to-head 3 (baseline): Perfect-info Prowess (A) vs Heuristic Burn (B)
        var (piWins, piDecided, piDraws, piInc) = await RunHeadToHead(
            label: "perfectinfo-vs-heuristic",
            seatA: PerfectInfoBot, seatADeck: BotDeck,
            seatB: HeuristicOpp, seatBDeck: OppDeck,
            seedBlock: BaseSeed + 2000);

        double infRate = infDecided > 0 ? (double)infWins / infDecided : 0.0;
        double ipRate  = ipDecided  > 0 ? (double)ipWins  / ipDecided  : 0.0;
        double piRate  = piDecided  > 0 ? (double)piWins  / piDecided  : 0.0;

        // ── Single grep-able summary line for the controller ─────────────────────
        _out.WriteLine(
            $"[INFER] botDeck={BotDeck} oppDeck={OppDeck} N={Games}  " +
            $"infer-vs-heuristic={infRate:P0} ({infWins}/{infDecided})  " +
            $"infer-vs-perfectinfo={ipRate:P0} ({ipWins}/{ipDecided})  " +
            $"perfectinfo-vs-heuristic={piRate:P0} ({piWins}/{piDecided})  " +
            $"draws=h1:{infDraws},h2:{ipDraws},h3:{piDraws}  " +
            $"inconclusive=h1:{infInc},h2:{ipInc},h3:{piInc}  " +
            $"iter={MctsIterations} budgetMs={MctsBudgetMs} maxTurns={MaxTurns} prioritySearch=true");

        // ── Liveness-only assertions (NOT a win% threshold) ──────────────────────
        // Each head-to-head must have produced at least one decided game so the
        // logged win-rates are meaningful, and the rates must be finite [0,1]. The
        // controller makes the strength judgment from the [INFER] line above.
        infDecided.Should().BeGreaterThan(0,
            "infer-vs-heuristic must decide at least one game for its win-rate to be meaningful");
        ipDecided.Should().BeGreaterThan(0,
            "infer-vs-perfectinfo must decide at least one game for its win-rate to be meaningful");
        piDecided.Should().BeGreaterThan(0,
            "perfectinfo-vs-heuristic must decide at least one game for its win-rate to be meaningful");

        infRate.Should().BeInRange(0.0, 1.0);
        ipRate.Should().BeInRange(0.0, 1.0);
        piRate.Should().BeInRange(0.0, 1.0);
    }

    // ── Head-to-head runner ─────────────────────────────────────────────────────

    /// <summary>
    /// Plays <see cref="Games"/> ASYMMETRIC games of seat-A-strategy (playing
    /// <paramref name="seatADeck"/>) vs seat-B-strategy (playing
    /// <paramref name="seatBDeck"/>), alternating which physical seat (Alice/Bob)
    /// hosts strategy A across games to cancel play/draw bias (mirrors
    /// <see cref="DeterminizedVsPerfectInfoTests"/>). Each seat's DECK travels with
    /// its strategy, so when seat A swaps to Bob its deck swaps too. Returns
    /// (aWins, decided, draws, inconclusive). Each game uses a distinct fixed seed
    /// <c>seedBlock + i</c> for reproducible variety.
    /// </summary>
    private async Task<(int AWins, int Decided, int Draws, int Inconclusive)> RunHeadToHead(
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
    /// Run one ASYMMETRIC game of seat-A-strategy (deck <paramref name="seatADeck"/>)
    /// vs seat-B-strategy (deck <paramref name="seatBDeck"/>) and return which
    /// strategy won (or Draw at the turn cap / Inconclusive on an engine crash).
    /// Each seat's DECK is loaded via <c>DeckLoader.LoadReal(thatSeatsArchetype, Repo)</c>
    /// — the bot-under-test plays its own archetype, the opponent plays a different
    /// one — so the board develops normally and the inference bot has a genuinely
    /// different opponent deck to identify. The deck follows the strategy when seat
    /// A hosts Bob (asymmetric: the two decks differ). A single crashed game is
    /// counted Inconclusive and logged so it cannot abort the whole run.
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
        // Alice, else seat-B's deck. This keeps the matchup asymmetric and correct
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

        // 6-minute per-game cap; the inference bot's belief-allocated K-world loop
        // can be slower than the single-tree perfect-info bot.
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
