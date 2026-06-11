using Majik.Bot;
using Majik.Bot.Tests.Integration.Helpers;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Search;

/// <summary>
/// Diagnostic probe: does the bot now PILOT the Belcher ritual-combo deck after
/// the mana-availability fix?
///
/// <para>
/// <b>Context:</b> The bot's mana-availability check previously counted only
/// lands in play; it now counts floating mana pool + mana dorks/rocks as well.
/// Belcher is a NO-LANDS deck — 100% rituals, cantrips, and the Goblin
/// Charbelcher ({4}) + activation ({3},{T}) combo. Before the fix every game
/// durdled: the bot saw 0 lands, believed it had 0 mana, never cast anything,
/// and every game ended as a draw at the turn cap.
/// </para>
///
/// <para>
/// <b>What this probe measures:</b>
/// <list type="bullet">
///   <item><b>Decided rate:</b> did any game end with someone at 0 life?
///     Before fix: ~0 decided / 12 games (all draws). After fix: expect
///     some decided games as the bot starts casting spells.</item>
///   <item><b>Avg opponent life at game end:</b> proxy for "did anyone deal
///     damage / belch?". Before: always 20. After: should drop toward 0
///     in decided games.</item>
///   <item><b>Goblin Charbelcher cast count:</b> how many games ended with
///     Charbelcher on the battlefield OR in a graveyard of either player?
///     Inspects the final game state across both players' battlefield +
///     graveyard. This is the key signal: 0 before the fix, &gt;0 after.</item>
///   <item><b>Belch-activated proxy:</b> a game that decided quickly
///     (&lt;= 10 turns) with the opponent going from 20 to 0 (or very low)
///     strongly suggests a successful Charbelcher activation (lethal belch).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Setup:</b> MCTS bot (150 iter / 1500 ms, prioritySearch=true) vs
/// heuristic bot. Both seats run "Belcher" (no-lands ritual combo). 12 games,
/// maxTurns=30, deterministic seeds starting at 3000. Games alternate seats
/// so no systematic first-player bias.
/// </para>
///
/// <para><b>Run command:</b>
/// <code>
/// dotnet test Majik.Bot.Tests.Integration --filter "~BelcherPilot" --no-build -v n
/// </code>
/// (Remove the Skip attribute first, or use the explicit override below.)
/// </para>
/// </summary>
public sealed class BelcherPilotDiagnosticTests
{
    private readonly ITestOutputHelper _out;

    /// <summary>
    /// Embedded card repository — loaded once per class. LoadReal resolves card
    /// names to proper typed shells (correct mana costs, subtypes, etc.) so the
    /// binder chain can wire up implemented abilities at game-start.
    /// </summary>
    private static readonly EmbeddedCardRepository Repo = new();

    private const int MctsIterations  = 150;
    private const int MctsBudgetMs    = 1500;
    private const int Games            = 12;
    private const int MaxTurns         = 30;
    private const int BaseSeed         = 3000;
    private const string Archetype     = "Belcher";

    // Quick-belch heuristic: if the game decides in ≤ this many turns AND
    // opponent life dropped below this threshold, count it as a probable
    // Charbelcher activation (ritual chain → {4} cast → {3}{T} activation
    // typically happens turn 1–3; opponent goes from 20 to ~0 in one swing).
    private const int BelchTurnThreshold = 10;
    private const int BelchLifeThreshold = 5;

    public BelcherPilotDiagnosticTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private enum GameWinner { Mcts, Heuristic, Draw, Inconclusive }

    /// <summary>
    /// Per-game snapshot capturing the signals needed to evaluate whether the
    /// bot can now pilot Belcher after the mana-availability fix.
    /// </summary>
    private sealed record BelcherGameSnapshot(
        int  GameIndex,
        GameWinner Winner,
        int  MctsLife,
        int  HeuristicLife,
        int  TurnsPlayed,
        /// <summary>
        /// True if Goblin Charbelcher appears anywhere on the final battlefield
        /// or in either player's graveyard — meaning it was cast at least once
        /// during the game. This is the primary post-fix signal: before the
        /// mana-availability fix, 0 games saw Charbelcher cast.
        /// </summary>
        bool CharbelcherWasCast,
        /// <summary>
        /// Heuristic proxy: a decided game with few turns + low opponent life
        /// strongly suggests a successful belch activation (ritual chain into
        /// {3}{T} activation dealing ~20+ to the opponent). Not conclusive —
        /// also fires on a lucky heuristic win — but meaningful in aggregate.
        /// </summary>
        bool LikelyBelchActivated);

    /// <summary>
    /// On-demand Belcher pilot diagnostic. Un-skip and run to check whether
    /// the mana-availability fix (count pool+dorks, not just lands) enables
    /// the bot to cast spells in the no-lands Belcher deck.
    ///
    /// <para>Expected BEFORE fix: all draws, 0 Charbelcher cast, avg opp life=20.</para>
    /// <para>Expected AFTER fix:  some decided games, Charbelcher cast in some games,
    /// avg opp life &lt; 20 (ideally near 0 in decided games).</para>
    /// </summary>
    [Fact(Skip = "on-demand diagnostic — un-skip to run the 12-game Belcher pilot probe")]
    public async Task Belcher_BotActuallyPilots_Diagnostic()
    {
        int mctsWins = 0, heuristicWins = 0, draws = 0, inconclusive = 0;
        int charbelcherCastGames = 0;
        int likelyBelchActivatedGames = 0;
        var snapshots = new List<BelcherGameSnapshot>();

        for (int i = 0; i < Games; i++)
        {
            bool mctsOnPlay = i % 2 == 0;
            int seed = BaseSeed + i;

            var snap = await PlayOneBelcherGame(
                mctsOnPlay: mctsOnPlay,
                seed:       seed,
                gameIndex:  i);

            snapshots.Add(snap);

            if (snap.CharbelcherWasCast) charbelcherCastGames++;
            if (snap.LikelyBelchActivated) likelyBelchActivatedGames++;

            switch (snap.Winner)
            {
                case GameWinner.Mcts:        mctsWins++;      break;
                case GameWinner.Heuristic:   heuristicWins++; break;
                case GameWinner.Draw:        draws++;         break;
                case GameWinner.Inconclusive: inconclusive++; break;
            }

            _out.WriteLine(
                $"  [BELCHER] game {i,2}: seed={seed} mctsOnPlay={mctsOnPlay} " +
                $"result={snap.Winner} turns={snap.TurnsPlayed} " +
                $"life=mcts:{snap.MctsLife} heu:{snap.HeuristicLife} " +
                $"charbelcher={snap.CharbelcherWasCast} belchProxy={snap.LikelyBelchActivated}");
        }

        int decided = mctsWins + heuristicWins;

        // Average opponent (heuristic) life at end across ALL non-inconclusive games.
        // In decided games where the MCTS bot won by belching, heuristic life → 0.
        var measured = snapshots.Where(s => s.Winner != GameWinner.Inconclusive).ToList();
        double avgHeuLife  = measured.Count > 0 ? measured.Average(s => s.HeuristicLife) : 20.0;
        double avgMctsLife = measured.Count > 0 ? measured.Average(s => s.MctsLife)      : 20.0;

        // ── DIAGNOSTIC SUMMARY LINE ──────────────────────────────────────────
        // Key signal: before fix → decided≈0, charbelcher≈0, avgHeuLife≈20.
        //             after fix  → decided>0, charbelcher>0, avgHeuLife<20.
        _out.WriteLine(string.Empty);
        _out.WriteLine(
            $"[BELCHER] decided={decided} draws={draws} (of {Games})  — " +
            $"mcts {mctsWins} / heuristic {heuristicWins}  | " +
            $"avg opp-life-at-end={avgHeuLife:F1}  | " +
            $"games-with-charbelcher-cast={charbelcherCastGames}  | " +
            $"games-with-belch-activated(proxy)={likelyBelchActivatedGames}");

        _out.WriteLine(
            $"[BELCHER] avg mcts-life={avgMctsLife:F1}  " +
            $"iter={MctsIterations} budgetMs={MctsBudgetMs} maxTurns={MaxTurns} " +
            $"deck={Archetype} prioritySearch=true");

        // NOTE: no FluentAssertions assertion here — this is a DIAGNOSTIC, not a
        // pass/fail gate. The test is always skipped in CI. Un-skip manually,
        // inspect the [BELCHER] summary line to evaluate the mana fix impact.
    }

    // ── Helper: play one Belcher game and return snapshot ───────────────────

    private async Task<BelcherGameSnapshot> PlayOneBelcherGame(
        bool mctsOnPlay, int seed, int gameIndex)
    {
        string aliceName = mctsOnPlay ? "MCTS"      : "Heuristic";
        string bobName   = mctsOnPlay ? "Heuristic" : "MCTS";

        // Both seats run the Belcher deck (no-lands ritual combo).
        // LoadReal resolves each card name from the embedded seed → correct
        // type/mana/subtypes so the binder chain can wire abilities.
        var facade = GameFacade.Create(
            aliceName: aliceName,
            bobName:   bobName,
            aliceDeck: DeckLoader.LoadReal(Archetype, Repo),
            bobDeck:   DeckLoader.LoadReal(Archetype, Repo),
            cardRepo:  Repo);

        var mctsConfig = new BotConfig(Archetype, Strategy: "mcts",
            RandomSeed: seed,
            MaxMctsIterations: MctsIterations,
            MaxMctsBudgetMs: MctsBudgetMs,
            PrioritySearchEnabled: true);

        var heuristicConfig = new BotConfig(Archetype, Strategy: "heuristic",
            RandomSeed: seed + 500);

        Player mctsPlayer;
        Player heuristicPlayer;

        if (mctsOnPlay)
        {
            facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, mctsConfig));
            facade.ReplaceBobAgent(  new BotPlayerAgent(facade.Bob,   heuristicConfig));
            mctsPlayer      = facade.Alice;
            heuristicPlayer = facade.Bob;
        }
        else
        {
            facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, heuristicConfig));
            facade.ReplaceBobAgent(  new BotPlayerAgent(facade.Bob,   mctsConfig));
            mctsPlayer      = facade.Bob;
            heuristicPlayer = facade.Alice;
        }

        // 5-minute per-game wall-clock safety cap.
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            await facade.StartFullGameAsync(
                maxTurns: MaxTurns,
                ct:       cts.Token,
                rng:      new GameRandom(seed));

            var result = await facade.FullGameTask!;

            // ── Post-game state inspection ───────────────────────────────
            // Clamp life totals that went negative (player lost) to 0 for display.
            int mctsLife      = Math.Max(0, mctsPlayer.LifeTotal);
            int heuristicLife = Math.Max(0, heuristicPlayer.LifeTotal);

            // Charbelcher-cast detection: scan ALL four terminal zones
            // (battlefield + graveyard for each player) for the permanent
            // name "Goblin Charbelcher".
            // If Charbelcher was cast this game, it must end up in one of:
            //   - a player's battlefield (still in play)
            //   - a player's graveyard   (was cast then destroyed/sacrificed)
            // Note: Exile is not searched (unlikely after a belch activation,
            // and not a typical Charbelcher exit zone in the Belcher list).
            bool charbelcherCast = CardNameInZone(facade.Alice, ZoneType.Battlefield, "Goblin Charbelcher")
                                || CardNameInZone(facade.Alice, ZoneType.Graveyard,   "Goblin Charbelcher")
                                || CardNameInZone(facade.Bob,   ZoneType.Battlefield, "Goblin Charbelcher")
                                || CardNameInZone(facade.Bob,   ZoneType.Graveyard,   "Goblin Charbelcher");

            // Belch-activation proxy: a decided game that ended quickly with
            // the losing player well below 20 life. This fires when either bot
            // successfully belches — we care that SOMEONE closed a game fast.
            bool likelyBelch = result.Winner != null
                && result.TurnsPlayed <= BelchTurnThreshold
                && (heuristicLife <= BelchLifeThreshold || mctsLife <= BelchLifeThreshold);

            GameWinner winner;
            if (result.Winner == null)
            {
                winner = GameWinner.Draw;
            }
            else
            {
                bool mctsWon = ReferenceEquals(result.Winner, mctsPlayer);
                winner = mctsWon ? GameWinner.Mcts : GameWinner.Heuristic;
            }

            return new BelcherGameSnapshot(
                GameIndex:            gameIndex,
                Winner:               winner,
                MctsLife:             mctsLife,
                HeuristicLife:        heuristicLife,
                TurnsPlayed:          result.TurnsPlayed,
                CharbelcherWasCast:   charbelcherCast,
                LikelyBelchActivated: likelyBelch);
        }
        catch (Exception ex)
        {
            _out.WriteLine(
                $"  [BELCHER] game {gameIndex,2}: INCONCLUSIVE — {ex.GetType().Name}: {ex.Message}");
            _out.WriteLine(
                $"    stack: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");

            return new BelcherGameSnapshot(
                GameIndex:            gameIndex,
                Winner:               GameWinner.Inconclusive,
                MctsLife:             20,
                HeuristicLife:        20,
                TurnsPlayed:          0,
                CharbelcherWasCast:   false,
                LikelyBelchActivated: false);
        }
    }

    // ── Utility ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if any card in the given zone of <paramref name="player"/>
    /// has <see cref="Majik.Core.Cards.ICard.Name"/> == <paramref name="cardName"/>.
    /// Used to detect Goblin Charbelcher at game-end without requiring a dedicated
    /// event subscriber (the post-game board state is still fully intact).
    /// </summary>
    private static bool CardNameInZone(Player player, ZoneType zone, string cardName)
        => player.Zones.GetZone(zone).GetCards()
                       .Any(c => string.Equals(c.Name, cardName, StringComparison.OrdinalIgnoreCase));
}
