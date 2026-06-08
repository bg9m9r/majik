using FluentAssertions;
using Majik.Bot;
using Majik.Bot.Tests.Integration.Helpers;
using Majik.Core.Api;
using Majik.Core.Random;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Search;

/// <summary>
/// Head-to-head strength regression: MCTS search bot vs heuristic bot.
///
/// <para>
/// Both seats play the same "Burn" deck (vanilla-fallback loader — the
/// simplest deck with lands and creatures). Seat assignment alternates every
/// game so neither strategy gets a systematic first-player advantage. Each
/// game uses a deterministic seed so the suite is fully reproducible.
/// </para>
///
/// <para>
/// <b>CRITICAL FINDING (2026-06-07):</b> At 100 MCTS iterations / 200 ms
/// budget per combat decision with priority search disabled, the MCTS combat
/// search does NOT beat the heuristic bot. Measured win rate across two
/// configurations:
/// <list type="bullet">
///   <item>Vanilla fallback deck (<c>DeckLoader.Load</c>): search 0/6 decided
///     (0.0%), 14 draws, runtime ~44 s.</item>
///   <item>Real cards (<c>DeckLoader.LoadReal</c>): search 1/6 decided (16.7%),
///     14 draws, runtime ~18 minutes — too slow for regular CI.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Root cause analysis:</b> The heuristic bot's <c>CombatSearch</c>
/// performs an explicit minimax over all attacker subsets (greedy + full
/// opponent-block enumeration for small boards), producing precise
/// deterministic combat outcomes. The MCTS search with
/// <c>DepthTurns=1</c> and 100 iterations produces only rough estimates of
/// the same outcomes via simulation. On a Burn mirror match with many 1/1
/// creatures, <c>CombatSearch</c> correctly evaluates "don't attack into
/// equal blockers" while the MCTS's noisy rollouts sometimes misclassify
/// the attack as neutral or good. The heuristic wins ALL decided games.
///
/// Additionally, the priority MCTS (<c>PrioritySearchEnabled=false</c> in
/// this test) was disabled because sandbox games starting from main phase
/// trigger the priority-loop safety (500-action limit) on unimplemented
/// Burn spells, causing each MCTS priority call to take minutes rather
/// than milliseconds. This means the search bot only differs from the
/// heuristic in combat attack planning — where it underperforms.
/// </para>
///
/// <para>
/// <b>What to do:</b>
/// <list type="number">
///   <item>The assertion below (<c>&gt; 0.50</c>) is the correct bar for the
///     Phase 1 MCTS to be considered superior. The test currently FAILS,
///     which is the honest result — do NOT change the assertion to force
///     it green.</item>
///   <item>To make this test pass, the MCTS must either use more iterations
///     (production default 200), fix the priority-loop issue in sandbox
///     games (so priority MCTS can contribute), or improve the rollout
///     quality (deeper evaluation function, longer rollout depth).</item>
///   <item>The <c>[STRENGTH]</c> line in test output reports the exact
///     measured win rate each run.</item>
/// </list>
/// </para>
/// </summary>
public sealed class SearchVsHeuristicTests
{
    private readonly ITestOutputHelper _out;

    /// <summary>
    /// MCTS iteration cap for this test. 100 iterations keeps each attack
    /// decision bounded at <see cref="MctsBudgetMs"/> ms so a 20-game suite
    /// finishes in under a minute.
    ///
    /// <para>
    /// 200 iterations (production default) take longer but produce the same
    /// losing result. The bottleneck is not iteration count but the
    /// fundamental mismatch between the rollout depth and the decision quality
    /// needed on a creature-heavy mirror board.
    /// </para>
    /// </summary>
    private const int MctsIterations = 100;

    /// <summary>
    /// Wall-clock budget per MCTS search call in milliseconds.
    /// Caps each combat decision at 200 ms; with 100 iterations this is
    /// the binding constraint so each search call exits quickly.
    /// </summary>
    private const int MctsBudgetMs = 200;

    /// <summary>Number of head-to-head games to play.</summary>
    private const int Games = 20;

    /// <summary>Maximum turns per game — prevents hangs on drawn-out games.</summary>
    private const int MaxTurns = 30;

    /// <summary>Base seed; game i uses seed <c>BaseSeed + i</c>.</summary>
    private const int BaseSeed = 2000;

    /// <summary>Deck archetype used by both seats.</summary>
    private const string Archetype = "Burn";

    public SearchVsHeuristicTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private enum GameWinner { Search, Heuristic, Draw }

    /// <summary>
    /// Play <see cref="Games"/> head-to-head games of search vs heuristic
    /// and assert the search bot wins a majority of decided games.
    ///
    /// <para>
    /// <b>CRITICAL FINDING:</b> This assertion currently FAILS. The search
    /// bot wins 0/6 decided games (0%) on the vanilla-fallback Burn mirror.
    /// See class-level doc for root cause analysis. The test is left with the
    /// correct bar (<c>&gt; 0.50</c>) so that the failure is visible and
    /// explicit. Do NOT loosen the assertion to force green.
    /// </para>
    ///
    /// <para>
    /// "Decided" games are those where one bot's life total reached 0.
    /// Games that hit <see cref="MaxTurns"/> are draws and are excluded
    /// from the win-rate denominator.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SearchBot_BeatsHeuristicBot_HeadToHead()
    {
        int searchWins = 0, heuristicWins = 0, draws = 0;

        for (int i = 0; i < Games; i++)
        {
            // Alternate who is nominally "on the play" by assigning seats;
            // the actual first-mover is determined by the game RNG (coin flip)
            // from the seed, but alternating seats across games ensures no
            // systematic seat bias in the win-rate measurement.
            bool searchOnPlay = i % 2 == 0;
            int seed = BaseSeed + i;

            var outcome = await PlayOneGame(searchOnPlay: searchOnPlay, seed: seed);

            switch (outcome)
            {
                case GameWinner.Search:    searchWins++;    break;
                case GameWinner.Heuristic: heuristicWins++; break;
                case GameWinner.Draw:      draws++;          break;
            }

            _out.WriteLine(
                $"  game {i,2}: seed={seed} search={( searchOnPlay ? "A(play)" : "B(draw)" )} " +
                $"result={outcome}  cumulative: search {searchWins} heuristic {heuristicWins} draw {draws}");
        }

        int decided = searchWins + heuristicWins;
        double winRate = decided > 0 ? (double)searchWins / decided : 0.0;

        _out.WriteLine(
            $"[STRENGTH] search {searchWins}/{decided} decided ({Games} played, {draws} draws) " +
            $"win-rate={winRate:P1}  " +
            $"(CRITICAL FINDING: search does NOT beat heuristic at {MctsIterations} iterations / {MctsBudgetMs} ms budget)");

        // The suite must have at least one decided game — if all are draws
        // (e.g. max-turns hit every time) the strength assertion is meaningless.
        decided.Should().BeGreaterThan(0,
            "at least one game must be decided for the strength assertion to apply");

        // CRITICAL FINDING: this assertion FAILS. Measured rate = 0/6 (0%)
        // on the vanilla-fallback Burn mirror. Do NOT change to force green.
        // The failing test is the honest documentation of the finding:
        // the Phase 1 MCTS combat search does not yet beat the heuristic.
        // To make this green, increase iterations (reduce noise) and/or fix
        // the priority-loop issue so priority MCTS can also contribute.
        winRate.Should().BeGreaterThan(0.50,
            $"[CRITICAL] search bot should win a strict majority of decided games; " +
            $"actual: {searchWins}/{decided} ({winRate:P1}). " +
            $"Root cause: MCTS rollout with DepthTurns=1 and {MctsIterations} iterations " +
            $"cannot distinguish good from bad attacks as accurately as the heuristic " +
            $"CombatSearch minimax. Priority MCTS is also disabled in this test " +
            $"(priority sandbox games hit the 500-action loop on unimplemented Burn spells). " +
            $"See SearchVsHeuristicTests XML doc for full analysis.");
    }

    // ── Helper: play a single game and return which strategy won ────────────

    /// <summary>
    /// Run one game of search vs heuristic, returning which strategy won (or
    /// <see cref="GameWinner.Draw"/> if the turn cap was reached with no winner).
    ///
    /// <para>
    /// Uses <c>DeckLoader.Load</c> (vanilla-fallback) to keep the test fast
    /// (~44 s for 20 games). Real cards (<c>LoadReal</c>) take ~18 minutes
    /// because unimplemented Burn spells trigger the priority-loop safety in
    /// the engine even with priority MCTS disabled. The fallback deck has
    /// basic lands and creatures so combat is meaningful.
    /// </para>
    ///
    /// <para>
    /// <c>PrioritySearchEnabled: false</c> is set on the search config to
    /// prevent the priority MCTS sandbox games from starting in main phase,
    /// where they trigger the 500-action priority loop. The search bot
    /// therefore only differs from the heuristic in its attack decisions
    /// (MCTS-backed <c>PickAttackers</c>) and block decisions
    /// (<c>BlockCombatEval</c>).
    /// </para>
    /// </summary>
    private static async Task<GameWinner> PlayOneGame(bool searchOnPlay, int seed)
    {
        string aliceName = searchOnPlay ? "Search"    : "Heuristic";
        string bobName   = searchOnPlay ? "Heuristic" : "Search";

        // Vanilla-fallback deck: basic lands + 1/1 creatures @ {1}{R}.
        // Fast and deterministic; no ability interactions that trigger loops.
        var facade = GameFacade.Create(
            aliceName: aliceName,
            bobName:   bobName,
            aliceDeck: DeckLoader.Load(Archetype),
            bobDeck:   DeckLoader.Load(Archetype));

        // MCTS config: capped iterations + wall-clock budget + priority MCTS
        // disabled (priority sandbox games hit the priority-loop safety limit
        // on unimplemented Burn instants, causing multi-minute per-call latency).
        var searchConfig    = new BotConfig(Archetype, Strategy: "mcts",
            RandomSeed: seed,
            MaxMctsIterations: MctsIterations,
            MaxMctsBudgetMs: MctsBudgetMs,
            PrioritySearchEnabled: false);
        var heuristicConfig = new BotConfig(Archetype, Strategy: "heuristic",
            RandomSeed: seed + 500);

        if (searchOnPlay)
        {
            facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, searchConfig));
            facade.ReplaceBobAgent(  new BotPlayerAgent(facade.Bob,   heuristicConfig));
        }
        else
        {
            facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, heuristicConfig));
            facade.ReplaceBobAgent(  new BotPlayerAgent(facade.Bob,   searchConfig));
        }

        // 3-minute cap per game; 20 games should finish well under 5 minutes total.
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        await facade.StartFullGameAsync(
            maxTurns: MaxTurns,
            ct: cts.Token,
            rng: new GameRandom(seed));

        var result = await facade.FullGameTask!;

        if (result.Winner == null)
            return GameWinner.Draw;

        // Map winner → strategy by checking which seat held the search bot.
        bool searchWon = searchOnPlay
            ? ReferenceEquals(result.Winner, facade.Alice)  // Alice = search
            : ReferenceEquals(result.Winner, facade.Bob);   // Bob = search

        return searchWon ? GameWinner.Search : GameWinner.Heuristic;
    }
}
