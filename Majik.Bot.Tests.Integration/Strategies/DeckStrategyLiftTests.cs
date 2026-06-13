using Majik.Bot;
using Majik.Bot.Search;
using Majik.Bot.Strategies;
using Majik.Bot.Tests.Integration.Helpers;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Strategies;

/// <summary>
/// Controlled deck-strategy lift probe: same deck (GrixisReanimator), MCTS both
/// seats, one seat WITH the real <see cref="GrixisReanimatorStrategy"/>, the other
/// WITH a <see cref="NullDeckStrategy"/> sentinel that keeps the deck-strategy slot
/// non-null (preventing registry auto-resolution) but contributes zero eval bonus,
/// zero win-line directives, and no mulligan advice.
///
/// <para>
/// <b>Why this measures lift cleanly:</b> both seats share the same deck, the same
/// MCTS config (150 iter / 1500 ms / maxTurns 30), the same seed family, and the
/// same SearchStrategy code path. The only variable is the <c>IDeckStrategy</c>
/// instance injected into the ON seat vs the OFF seat. Any systematic win-rate
/// difference above 50% is attributable to the strategy.
/// </para>
///
/// <para>
/// <b>Injection mechanism:</b> <see cref="SearchStrategy"/> has an internal
/// test-seam constructor <c>SearchStrategy(BotConfig, IDeckStrategy?)</c>.
/// Passing <c>new GrixisReanimatorStrategy()</c> enables the strategy; passing
/// <c>new NullDeckStrategy()</c> disables it (non-null short-circuits the registry
/// so the real strategy is NOT resolved). The two <see cref="SearchStrategy"/>
/// instances are then wrapped via the internal
/// <see cref="BotPlayerAgent(Player, IBotStrategy, Action{bool}?)"/> test-seam
/// constructor so each seat uses its prebuilt strategy directly.
/// </para>
///
/// <para>
/// The test is skipped by default (on-demand strength probe). Un-skip and run with
/// <c>--filter FullyQualifiedName~DeckStrategyLift</c> to execute the full 16-game
/// measurement. Re-skip before committing.
/// </para>
/// </summary>
public sealed class DeckStrategyLiftTests
{
    private readonly ITestOutputHelper _out;

    /// <summary>Embedded card repo — loaded once per class.</summary>
    private static readonly EmbeddedCardRepository Repo = new();

    /// <summary>GrixisReanimator archetype name — must match BotDeckCatalog key.</summary>
    private const string Archetype = "GrixisReanimator";

    /// <summary>MCTS iteration cap — matches SearchVsHeuristicTests for comparability.</summary>
    private const int MctsIterations = 150;

    /// <summary>Wall-clock budget per MCTS call — matches SearchVsHeuristicTests.</summary>
    private const int MctsBudgetMs = 1500;

    /// <summary>Number of games to play for the full lift measurement.</summary>
    private const int Games = 16;

    /// <summary>Turn cap per game — prevents hangs on drawn-out games.</summary>
    private const int MaxTurns = 30;

    /// <summary>Base seed; game i uses <c>BaseSeed + i</c> for the game RNG.</summary>
    private const int BaseSeed = 3000;

    private enum GameWinner { WithStrategy, WithoutStrategy, Draw, Inconclusive }

    public DeckStrategyLiftTests(ITestOutputHelper output)
    {
        _out = output;
    }

    /// <summary>
    /// Controlled lift probe: GrixisReanimator mirror, MCTS both seats.
    /// Seat A = WITH real <see cref="GrixisReanimatorStrategy"/>;
    /// Seat B = WITH <see cref="NullDeckStrategy"/> (strategy disabled).
    /// Seeds alternate which seat holds the strategy so seat bias cancels.
    ///
    /// <para>Logs a <c>[LIFT]</c> summary line. Un-skip + run to measure.</para>
    /// </summary>
    [Fact(Skip = "on-demand strength probe — run manually with --filter FullyQualifiedName~DeckStrategyLift to measure GrixisReanimator strategy lift")]
    public async Task GrixisReanimator_WithStrategy_BeatsWithoutStrategy()
    {
        int withWins = 0, withoutWins = 0, draws = 0, inconclusive = 0;
        int comboFired = 0;

        for (int i = 0; i < Games; i++)
        {
            // Alternate which seat is WITH-strategy each game to cancel seat bias.
            bool withStrategyIsAlice = i % 2 == 0;
            int seed = BaseSeed + i;

            var (outcome, firedThisGame) = await PlayOneGame(
                withStrategyIsAlice: withStrategyIsAlice,
                seed: seed,
                gameIndex: i,
                output: _out);

            if (firedThisGame) comboFired++;

            switch (outcome)
            {
                case GameWinner.WithStrategy:    withWins++;    break;
                case GameWinner.WithoutStrategy: withoutWins++; break;
                case GameWinner.Draw:            draws++;       break;
                case GameWinner.Inconclusive:    inconclusive++; break;
            }

            _out.WriteLine(
                $"  game {i,2}: seed={seed} withStrategy={(withStrategyIsAlice ? "Alice" : "Bob")} " +
                $"result={outcome} comboFiredThisGame={firedThisGame}  " +
                $"cumulative: with={withWins} without={withoutWins} draw={draws} inconclusive={inconclusive}");
        }

        int decided = withWins + withoutWins;
        double winRate = decided > 0 ? (double)withWins / decided : 0.0;

        _out.WriteLine(
            $"[LIFT] withStrategy {withWins}/{decided} decided ({Games} played, {draws} draws, {inconclusive} inconclusive) " +
            $"winrate={winRate:P1}  combo-fired={comboFired} games  " +
            $"deck={Archetype} iter={MctsIterations} budgetMs={MctsBudgetMs} maxTurns={MaxTurns}");
    }

    /// <summary>
    /// Controlled lift probe: Belcher mirror, MCTS both seats.
    /// Seat A = WITH real <see cref="BelcherComboSolver"/> (DIRECTIVE atomic kill);
    /// Seat B = WITH <see cref="NullDeckStrategy"/> (strategy disabled).
    /// Seeds alternate which seat holds the strategy so seat bias cancels.
    ///
    /// <para>
    /// This is the decisive test of whether DIRECTIVE strategies help on combo
    /// decks the search cannot pilot: the Charbelcher activation deals ~50
    /// damage in one resolution — the MCTS search cannot see this line (eval
    /// does not model "activate artifact = lethal"). The strategy fires the
    /// activation when it is payable; without it the bot never activates
    /// Charbelcher.
    /// </para>
    ///
    /// <para>Logs a <c>[LIFT]</c> summary line and a combo-fired count.
    /// Un-skip + run to measure.</para>
    /// </summary>
    [Fact(Skip = "on-demand strength probe — run manually with --filter FullyQualifiedName~DeckStrategyLift to measure Belcher directive atomic-kill strategy lift")]
    public async Task Belcher_WithStrategy_BeatsWithoutStrategy()
    {
        const string BelcherArchetype = "Belcher";

        int withWins = 0, withoutWins = 0, draws = 0, inconclusive = 0;
        int comboFired = 0;

        for (int i = 0; i < Games; i++)
        {
            bool withStrategyIsAlice = i % 2 == 0;
            int seed = BaseSeed + 100 + i; // distinct seed family from GrixisReanimator probe

            var (outcome, firedThisGame) = await PlayBelcherGame(
                withStrategyIsAlice: withStrategyIsAlice,
                seed: seed,
                gameIndex: i,
                output: _out);

            if (firedThisGame) comboFired++;

            switch (outcome)
            {
                case GameWinner.WithStrategy:    withWins++;     break;
                case GameWinner.WithoutStrategy: withoutWins++;  break;
                case GameWinner.Draw:            draws++;        break;
                case GameWinner.Inconclusive:    inconclusive++; break;
            }

            _out.WriteLine(
                $"  game {i,2}: seed={seed} withStrategy={(withStrategyIsAlice ? "Alice" : "Bob")} " +
                $"result={outcome} comboFiredThisGame={firedThisGame}  " +
                $"cumulative: with={withWins} without={withoutWins} draw={draws} inconclusive={inconclusive}");
        }

        int decided = withWins + withoutWins;
        double winRate = decided > 0 ? (double)withWins / decided : 0.0;

        _out.WriteLine(
            $"[LIFT] withStrategy {withWins}/{decided} decided ({Games} played, {draws} draws, {inconclusive} inconclusive) " +
            $"winrate={winRate:P1}  combo-fired(directive-activated)={comboFired} games  " +
            $"deck={BelcherArchetype} iter={MctsIterations} budgetMs={MctsBudgetMs} maxTurns={MaxTurns}");
    }

    // ── Helper — Belcher game ──────────────────────────────────────────────────

    private static async Task<(GameWinner Winner, bool ComboFired)> PlayBelcherGame(
        bool withStrategyIsAlice,
        int seed,
        int gameIndex,
        ITestOutputHelper output)
    {
        const string BelcherArchetype = "Belcher";

        string aliceName = withStrategyIsAlice ? "WithStrategy" : "WithoutStrategy";
        string bobName   = withStrategyIsAlice ? "WithoutStrategy" : "WithStrategy";

        var facade = GameFacade.Create(
            aliceName: aliceName,
            bobName:   bobName,
            aliceDeck: DeckLoader.LoadReal(BelcherArchetype, Repo),
            bobDeck:   DeckLoader.LoadReal(BelcherArchetype, Repo),
            cardRepo:  Repo);

        var mctsConfig = new BotConfig(
            BelcherArchetype,
            Strategy: "mcts",
            RandomSeed: seed,
            MaxMctsIterations: MctsIterations,
            MaxMctsBudgetMs: MctsBudgetMs,
            PrioritySearchEnabled: true);

        var instrumented = new InstrumentedDeckStrategy(new BelcherComboSolver());
        var withStrat    = new SearchStrategy(mctsConfig, deckOverride: instrumented);
        var withoutStrat = new SearchStrategy(mctsConfig, deckOverride: new NullDeckStrategy());

        Player withStrategyPlayer;
        Player withoutStrategyPlayer;

        if (withStrategyIsAlice)
        {
            facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, withStrat));
            facade.ReplaceBobAgent(  new BotPlayerAgent(facade.Bob,   withoutStrat));
            withStrategyPlayer    = facade.Alice;
            withoutStrategyPlayer = facade.Bob;
        }
        else
        {
            facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, withoutStrat));
            facade.ReplaceBobAgent(  new BotPlayerAgent(facade.Bob,   withStrat));
            withStrategyPlayer    = facade.Bob;
            withoutStrategyPlayer = facade.Alice;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            await facade.StartFullGameAsync(
                maxTurns: MaxTurns,
                ct: cts.Token,
                rng: new Majik.Core.Random.GameRandom(seed));

            var result = await facade.FullGameTask!;

            GameWinner winner;
            if (result.Winner == null)
            {
                winner = GameWinner.Draw;
            }
            else
            {
                bool withWon = ReferenceEquals(result.Winner, withStrategyPlayer);
                winner = withWon ? GameWinner.WithStrategy : GameWinner.WithoutStrategy;
            }

            return (winner, instrumented.WinLineEverFired);
        }
        catch (Exception ex)
        {
            output.WriteLine(
                $"  game {gameIndex,2}: INCONCLUSIVE — {ex.GetType().Name}: {ex.Message}");
            output.WriteLine($"    stack: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
            return (GameWinner.Inconclusive, instrumented.WinLineEverFired);
        }
    }

    // ── Helper — GrixisReanimator game ─────────────────────────────────────────

    /// <summary>
    /// Build and run one game. Returns the winner enum plus a flag indicating
    /// whether the with-strategy bot's <see cref="GrixisReanimatorStrategy"/>
    /// fired a win-line directive (<c>TryGetNextWinningAction</c> returned
    /// non-null at least once) this game.
    ///
    /// <para>
    /// The combo-fired flag is tracked via an instrumented wrapper around the
    /// real strategy — see <see cref="InstrumentedDeckStrategy"/>.
    /// </para>
    /// </summary>
    private static async Task<(GameWinner Winner, bool ComboFired)> PlayOneGame(
        bool withStrategyIsAlice,
        int seed,
        int gameIndex,
        ITestOutputHelper output)
    {
        string aliceName = withStrategyIsAlice ? "WithStrategy" : "WithoutStrategy";
        string bobName   = withStrategyIsAlice ? "WithoutStrategy" : "WithStrategy";

        // Both seats use the real GrixisReanimator deck (mirror).
        var facade = GameFacade.Create(
            aliceName: aliceName,
            bobName:   bobName,
            aliceDeck: DeckLoader.LoadReal(Archetype, Repo),
            bobDeck:   DeckLoader.LoadReal(Archetype, Repo),
            cardRepo:  Repo);

        // Shared MCTS config for both seats — the only difference is the IDeckStrategy.
        var mctsConfig = new BotConfig(
            Archetype,
            Strategy: "mcts",
            RandomSeed: seed,
            MaxMctsIterations: MctsIterations,
            MaxMctsBudgetMs: MctsBudgetMs,
            PrioritySearchEnabled: true);

        // ON seat: real GrixisReanimatorStrategy — tracks whether win-line fired.
        var instrumented = new InstrumentedDeckStrategy(new GrixisReanimatorStrategy());
        var withStrat    = new SearchStrategy(mctsConfig, deckOverride: instrumented);

        // OFF seat: NullDeckStrategy — non-null so registry lookup is bypassed,
        // but all three IDeckStrategy methods return neutral (0 / null / null).
        var withoutStrat = new SearchStrategy(mctsConfig, deckOverride: new NullDeckStrategy());

        // Assign seats using the internal IBotStrategy injection seam.
        Player withStrategyPlayer;
        Player withoutStrategyPlayer;

        if (withStrategyIsAlice)
        {
            facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, withStrat));
            facade.ReplaceBobAgent(  new BotPlayerAgent(facade.Bob,   withoutStrat));
            withStrategyPlayer    = facade.Alice;
            withoutStrategyPlayer = facade.Bob;
        }
        else
        {
            facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, withoutStrat));
            facade.ReplaceBobAgent(  new BotPlayerAgent(facade.Bob,   withStrat));
            withStrategyPlayer    = facade.Bob;
            withoutStrategyPlayer = facade.Alice;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            await facade.StartFullGameAsync(
                maxTurns: MaxTurns,
                ct: cts.Token,
                rng: new GameRandom(seed));

            var result = await facade.FullGameTask!;

            GameWinner winner;
            if (result.Winner == null)
            {
                winner = GameWinner.Draw;
            }
            else
            {
                bool withWon = ReferenceEquals(result.Winner, withStrategyPlayer);
                winner = withWon ? GameWinner.WithStrategy : GameWinner.WithoutStrategy;
            }

            return (winner, instrumented.WinLineEverFired);
        }
        catch (Exception ex)
        {
            output.WriteLine(
                $"  game {gameIndex,2}: INCONCLUSIVE — {ex.GetType().Name}: {ex.Message}");
            output.WriteLine($"    stack: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
            return (GameWinner.Inconclusive, instrumented.WinLineEverFired);
        }
    }

    // ── Instrumented wrapper ───────────────────────────────────────────────────

    /// <summary>
    /// Transparent decorator around an <see cref="IDeckStrategy"/> that records
    /// whether <see cref="TryGetNextWinningAction"/> returned a non-null action
    /// at least once during the game. All other calls are passed through unchanged.
    /// </summary>
    private sealed class InstrumentedDeckStrategy : IDeckStrategy
    {
        private readonly IDeckStrategy _inner;

        public bool WinLineEverFired { get; private set; }

        public InstrumentedDeckStrategy(IDeckStrategy inner)
        {
            _inner = inner;
        }

        public double StrategicScore(GameContext ctx, Player self)
            => _inner.StrategicScore(ctx, self);

        public PriorityAction? TryGetNextWinningAction(GameContext ctx, Player self)
        {
            var action = _inner.TryGetNextWinningAction(ctx, self);
            if (action is not null)
                WinLineEverFired = true;
            return action;
        }

        public MulliganDecision? AdviseMulligan(IReadOnlyList<ICard> hand, int mulligansTaken)
            => _inner.AdviseMulligan(hand, mulligansTaken);

        public IReadOnlyList<string> ReferencedCardNames => _inner.ReferencedCardNames;
    }
}
