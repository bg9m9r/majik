using Majik.Bot;
using Majik.Bot.Search;
using Majik.Bot.Strategies;
using Majik.Bot.Tests.Integration.Helpers;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Random;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Strategies;

/// <summary>
/// Belcher PILOT diagnostic — does the bot, after the #2623 MDFC back-face land
/// fix, actually DEVELOP MANA and ASSEMBLE the Charbelcher combo when piloting
/// the real Belcher deck?
///
/// <para>
/// Belcher runs ZERO true lands; every land in the list is the back face of an
/// MDFC (Shatterskull Smashing, Sundering Eruption, Razorgrass Ambush, etc.).
/// Before #2623 the enumerator never surfaced a back-land play at 0 mana → the
/// bot produced 0 mana all game (the prior re-measure was UNMEASURABLE). #2623
/// surfaces + chooses those plays. This probe measures whether the chain now
/// develops.
/// </para>
///
/// <para>
/// Both seats pilot Belcher with the real <see cref="BelcherStrategy"/> directive
/// (a pure pilot diagnostic — "can the bot pilot Belcher?", not a lift A/B). It
/// subscribes to the live event bus and records, PER GAME and across both seats:
/// </para>
/// <list type="bullet">
///   <item>LANDS PLAYED — distinct cards that entered a battlefield as a
///         <see cref="Land"/> (incl. MDFC back-land materializations), and the
///         max battlefield land count reached.</item>
///   <item>MANA PRODUCED — count of <see cref="ManaAbilityActivatedEvent"/>
///         (a mana source actually tapped for mana), plus max
///         <see cref="LegalActionEnumerator.UntappedManaSources"/> observed.</item>
///   <item>CHARBELCHER CAST — count of <see cref="SpellCastEvent"/> whose spell
///         card name is "Goblin Charbelcher". This is the CORRECT cast metric:
///         it counts the spell actually going on the stack, NOT a graveyard
///         presence (the old false-positive that counted cleanup discards).</item>
///   <item>BELCH ACTIVATED — count of <see cref="AbilityActivatedEvent"/> whose
///         source is Goblin Charbelcher (the {3},{T} activation).</item>
///   <item>COMBO KILL — game ended with a winner AND a belch fired this game.</item>
/// </list>
///
/// <para>Logs a <c>[BELCHER-DIAG]</c> per-game line and a <c>[BELCHER-SUMMARY]</c>
/// roll-up. Skipped by default; un-skip + run with
/// <c>--filter FullyQualifiedName~BelcherPilotDiagnostic</c>. Re-skip before commit.</para>
/// </summary>
public sealed class BelcherPilotEventDiagnosticTests
{
    private readonly ITestOutputHelper _out;

    private static readonly EmbeddedCardRepository Repo = new();

    private const string Archetype = "Belcher";
    private const string Charbelcher = "Goblin Charbelcher";
    private const int MctsIterations = 150;
    private const int MctsBudgetMs = 1500;
    private const int Games = 16;
    private const int MaxTurns = 30;
    private const int BaseSeed = 5000;

    public BelcherPilotEventDiagnosticTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private sealed class GameSignals
    {
        public readonly HashSet<ICard> LandsPlayed = new(ReferenceEqualityComparer.Instance);
        public int MaxBattlefieldLands;
        public int ManaProducedEvents;
        public int MaxUntappedManaSources;
        public int CharbelcherCasts;
        public int BelchActivations;
        public int LastTurn;
    }

    [Fact(Skip = "on-demand pilot diagnostic — run manually with --filter FullyQualifiedName~BelcherPilotEventDiagnostic to measure whether the bot develops mana + assembles the Charbelcher combo after #2623")]
    public async Task Belcher_Pilot_DevelopsManaAndAssemblesCombo()
    {
        int gamesWithLand = 0, gamesWithMana = 0, gamesWithCast = 0,
            gamesWithBelch = 0, comboKills = 0, decided = 0, inconclusive = 0;

        int totalLandsPlayed = 0, totalManaEvents = 0, totalCasts = 0, totalBelches = 0;
        int peakLands = 0, peakMana = 0;

        for (int i = 0; i < Games; i++)
        {
            int seed = BaseSeed + i;
            var (sig, hadWinner, ok) = await PlayBelcherPilotGame(seed, i);

            if (!ok) { inconclusive++; continue; }

            if (sig.LandsPlayed.Count > 0) gamesWithLand++;
            if (sig.ManaProducedEvents > 0 || sig.MaxUntappedManaSources > 0) gamesWithMana++;
            if (sig.CharbelcherCasts > 0) gamesWithCast++;
            if (sig.BelchActivations > 0) gamesWithBelch++;
            if (sig.BelchActivations > 0 && hadWinner) comboKills++;
            if (hadWinner) decided++;

            totalLandsPlayed += sig.LandsPlayed.Count;
            totalManaEvents  += sig.ManaProducedEvents;
            totalCasts       += sig.CharbelcherCasts;
            totalBelches     += sig.BelchActivations;
            peakLands = Math.Max(peakLands, sig.MaxBattlefieldLands);
            peakMana  = Math.Max(peakMana, sig.MaxUntappedManaSources);

            _out.WriteLine(
                $"[BELCHER-DIAG] game {i,2}: seed={seed} turns={sig.LastTurn} " +
                $"landsPlayed={sig.LandsPlayed.Count} maxBfLands={sig.MaxBattlefieldLands} " +
                $"manaEvents={sig.ManaProducedEvents} maxUntappedMana={sig.MaxUntappedManaSources} " +
                $"charbelcherCast={sig.CharbelcherCasts} belchActivated={sig.BelchActivations} " +
                $"winner={(hadWinner ? "Y" : "draw")}");
        }

        _out.WriteLine("");
        _out.WriteLine(
            $"[BELCHER-SUMMARY] games={Games} (inconclusive={inconclusive})  " +
            $"landsPlayed: {gamesWithLand}/{Games} games (total={totalLandsPlayed}, peakBf={peakLands})  " +
            $"manaProduced: {gamesWithMana}/{Games} games (manaEvents={totalManaEvents}, peakUntapped={peakMana})  " +
            $"CharbelcherCast(correct-metric): {gamesWithCast}/{Games} games (total={totalCasts})  " +
            $"belchActivated: {gamesWithBelch}/{Games} games (total={totalBelches})  " +
            $"comboKills: {comboKills}/{Games}  decided={decided}  " +
            $"deck={Archetype} iter={MctsIterations} budgetMs={MctsBudgetMs} maxTurns={MaxTurns}");
    }

    private static async Task<(GameSignals Signals, bool HadWinner, bool Ok)> PlayBelcherPilotGame(
        int seed, int gameIndex)
    {
        var facade = GameFacade.Create(
            aliceName: "PilotA",
            bobName:   "PilotB",
            aliceDeck: DeckLoader.LoadReal(Archetype, Repo),
            bobDeck:   DeckLoader.LoadReal(Archetype, Repo),
            cardRepo:  Repo);

        var mctsConfig = new BotConfig(
            Archetype,
            Strategy: "mcts",
            RandomSeed: seed,
            MaxMctsIterations: MctsIterations,
            MaxMctsBudgetMs: MctsBudgetMs,
            PrioritySearchEnabled: true);

        // Both seats pilot Belcher with the real directive strategy.
        facade.ReplaceAliceAgent(new BotPlayerAgent(
            facade.Alice, new SearchStrategy(mctsConfig, deckOverride: new BelcherStrategy())));
        facade.ReplaceBobAgent(new BotPlayerAgent(
            facade.Bob, new SearchStrategy(mctsConfig, deckOverride: new BelcherStrategy())));

        var sig = new GameSignals();

        // Subscribe to the live event bus for per-game signal capture.
        facade.EventBus.Subscribe<TurnStartedEvent>(e =>
        {
            sig.LastTurn = e.TurnNumber;
            // Sample untapped mana for both seats at each turn boundary.
            sig.MaxUntappedManaSources = Math.Max(
                sig.MaxUntappedManaSources,
                Math.Max(
                    LegalActionEnumerator.UntappedManaSources(facade.Alice),
                    LegalActionEnumerator.UntappedManaSources(facade.Bob)));
        });

        facade.EventBus.Subscribe<CardMovedEvent>(e =>
        {
            if (e.ToZone == Majik.Core.Zones.ZoneType.Battlefield && e.Card is Land)
                sig.LandsPlayed.Add(e.Card);

            // Recompute peak battlefield land count after any zone change.
            int bfLands =
                facade.Alice.Zones.Battlefield.GetCards().Count(c => c is Land) +
                facade.Bob.Zones.Battlefield.GetCards().Count(c => c is Land);
            sig.MaxBattlefieldLands = Math.Max(sig.MaxBattlefieldLands, bfLands);
        });

        facade.EventBus.Subscribe<ManaAbilityActivatedEvent>(_ => sig.ManaProducedEvents++);

        facade.EventBus.Subscribe<SpellCastEvent>(e =>
        {
            if (string.Equals(e.Spell.Card.Name, Charbelcher, StringComparison.Ordinal))
                sig.CharbelcherCasts++;
        });

        facade.EventBus.Subscribe<AbilityActivatedEvent>(e =>
        {
            if (e.Ability.Source is ICard src &&
                string.Equals(src.Name, Charbelcher, StringComparison.Ordinal))
                sig.BelchActivations++;
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            await facade.StartFullGameAsync(
                maxTurns: MaxTurns, ct: cts.Token, rng: new GameRandom(seed));
            var result = await facade.FullGameTask!;
            return (sig, result.Winner is not null, true);
        }
        catch
        {
            return (sig, false, false);
        }
    }
}
