using FluentAssertions;
using Majik.Bot;
using Majik.Bot.Heuristic;
using Majik.Bot.Strategies;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Strategies;

/// <summary>
/// Phase D (plan 2026-06-13) — the executor STRENGTH proof: the inverse of the
/// Belcher retest's 0/16. A bot piloting <c>AzoriusLotusBelcher</c> (heuristic +
/// the registry-resolved <see cref="BelcherComboSolver"/>) vs a passive goldfish
/// opponent must FIRE the Charbelcher combo and WIN within a tight turn bound,
/// across N seeded games.
///
/// <para>
/// This is a GOLDFISH proof of "the bot executes the combo," not "beats a deck":
/// the kill is seeded reachable (Charbelcher + Lotus Bloom pre-deployed, the
/// library is all-nonland MDFC fronts so the reveal is lethal) and the opponent
/// (<see cref="DeterministicBotAgent"/>) never acts. The combo-fired counter is
/// an instrumented wrapper around the real solver
/// (<see cref="TryGetNextWinningAction"/> returned non-null ≥ once).
/// </para>
///
/// <para>
/// Baseline being inverted: the bot retest measured 0/16 combo-fires with the
/// old non-firing strategy (it returned the belch only when {3} was already
/// FLOATING — which the WU deck's untapped-source mana never was, so the live
/// dispatch swallowed it). The solver floats first, so it fires.
/// </para>
///
/// <para>This is a [Fact] (not skipped): it is fast (heuristic, no MCTS budget;
/// the directive short-circuits before any search) and is the committed gate
/// that the executor actually fires.</para>
/// </summary>
public sealed class BelcherComboExecutorStrengthTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    private const string Archetype = "AzoriusLotusBelcher";

    /// <summary>Seeded games to play.</summary>
    private const int Games = 16;

    /// <summary>Tight turn bound — the kill is seeded reachable on the first
    /// main phase (Charbelcher + Lotus Bloom pre-deployed).</summary>
    private const int MaxTurns = 3;

    /// <summary>Opponent life — low enough that the seeded reveal is lethal
    /// (library holds far more nonland cards than this).</summary>
    private const int OpponentLife = 15;

    private readonly ITestOutputHelper _out;

    public BelcherComboExecutorStrengthTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task Bot_PilotingAzoriusLotusBelcher_FiresComboAndWins_VsGoldfish()
    {
        int wins = 0, comboFired = 0, draws = 0, inconclusive = 0;

        for (int i = 0; i < Games; i++)
        {
            int seed = 5000 + i;
            var (won, fired, status) = await PlayOneGame(seed, i);

            if (fired) comboFired++;
            switch (status)
            {
                case Status.Win: wins++; break;
                case Status.Draw: draws++; break;
                case Status.Inconclusive: inconclusive++; break;
            }

            _out.WriteLine(
                $"  game {i,2}: seed={seed} won={won} comboFired={fired} status={status}  " +
                $"cumulative: wins={wins} comboFired={comboFired} draws={draws} inconclusive={inconclusive}");
        }

        _out.WriteLine(
            $"[EXECUTOR] AzoriusLotusBelcher goldfish: wins={wins}/{Games} comboFired={comboFired}/{Games} " +
            $"draws={draws} inconclusive={inconclusive} maxTurns={MaxTurns}  (baseline retest: 0/16)");

        // The committed inversion of 0/16: the combo fires in the vast majority
        // of seeded games and converts to a win within the turn bound.
        comboFired.Should().BeGreaterThanOrEqualTo(14,
            "the solver must FIRE the Charbelcher combo (baseline was 0/16)");
        wins.Should().BeGreaterThanOrEqualTo(14,
            "firing the lethal belch must convert to a win within the turn bound");
    }

    // ── One game ─────────────────────────────────────────────────────────────

    private enum Status { Win, Draw, Inconclusive }

    private async Task<(bool Won, bool Fired, Status Status)> PlayOneGame(int seed, int gameIndex)
    {
        // Bot library: all-nonland MDFC fronts so a Charbelcher reveal walks the
        // whole library (lethal). A handful of real combo pieces keep the pile
        // representative; the load-bearing pieces are pre-deployed below.
        var botLibrary = BuildNonlandLibrary(40);
        var oppLibrary = Enumerable.Repeat("Island", 40).ToList();

        var facade = GameFacade.Create(
            aliceName: "Belcher",
            bobName: "Goldfish",
            aliceDeck: botLibrary.Select(BuildShell).ToList(),
            bobDeck: oppLibrary.Select(BuildShell).ToList(),
            cardRepo: Repo);

        SetLife(facade.Bob, OpponentLife);

        // Real production decision path: heuristic strategy for the archetype.
        // The deck strategy is the registry-resolved BelcherComboSolver, wrapped
        // in an instrumented decorator so we can observe combo-fires. (The
        // directive short-circuits before the heuristic, so heuristic vs mcts is
        // irrelevant to firing — heuristic is faster + deterministic.)
        var config = new BotConfig(Archetype, Strategy: "heuristic", RandomSeed: seed);
        var instrumented = new InstrumentedSolver(new BelcherComboSolver());
        var botStrategy = new HeuristicStrategy(config, deckOverride: instrumented);

        facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, botStrategy));
        facade.ReplaceBobAgent(new DeterministicBotAgent());

        // Pre-deploy the load-bearing combo: Charbelcher (the payoff) + Lotus
        // Bloom (the {3} engine). The solver floats Lotus Bloom, then belches.
        PlaceOnBattlefield(facade, facade.Alice, "Goblin Charbelcher");
        PlaceOnBattlefield(facade, facade.Alice, "Lotus Bloom");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await facade.StartFullGameAsync(
                firstPlayerSlot: 0, maxTurns: MaxTurns, ct: cts.Token,
                rng: new GameRandom(seed));
            var result = await facade.FullGameTask!;

            Status status = result.Winner == null
                ? Status.Draw
                : ReferenceEquals(result.Winner, facade.Alice) ? Status.Win : Status.Draw;
            return (status == Status.Win, instrumented.Fired, status);
        }
        catch (Exception ex)
        {
            _out.WriteLine($"  game {gameIndex,2}: INCONCLUSIVE — {ex.GetType().Name}: {ex.Message}");
            return (false, instrumented.Fired, Status.Inconclusive);
        }
    }

    // ── Seeding helpers (mirror ComboLineHarness) ────────────────────────────

    /// <summary>A library of <paramref name="count"/> nonland MDFC fronts
    /// (cycled) — every card is nonland by its front face (CR 712.4a) so the
    /// belch reveal is landless / lethal.</summary>
    private static IReadOnlyList<string> BuildNonlandLibrary(int count)
    {
        string[] fronts =
        {
            "Hydroelectric Specimen", "Jwari Disruption", "Sea Gate Restoration",
            "Sink into Stupor", "Razorgrass Ambush", "Waterlogged Teachings",
        };
        return Enumerable.Range(0, count).Select(i => fronts[i % fronts.Length]).ToList();
    }

    private static ICard BuildShell(string name) =>
        DeckCardShellBuilder.Build(
            Repo.GetByName(name)
            ?? throw new InvalidOperationException($"'{name}' not in embedded seed"));

    private static void PlaceOnBattlefield(GameFacade facade, Player owner, string name)
    {
        var live = DeckCardBuilder.BuildFromShell(
            shell: BuildShell(name),
            owner: owner,
            cardRepo: Repo,
            replacements: facade.Replacements,
            effects: facade.ContinuousEffects,
            routeThroughNamedFactories: true,
            triggers: facade.Triggers,
            zones: null,
            eventBus: facade.EventBus);
        live.SetOwner(owner);
        live.SetController(owner);
        live.SetZone(ZoneType.Battlefield);
        owner.Zones.GetZone(ZoneType.Battlefield).AddCard(live);
    }

    private static void SetLife(Player p, int life)
    {
        var delta = life - p.LifeTotal;
        if (delta < 0) p.LoseLife(-delta);
        else if (delta > 0) p.GainLife(delta);
    }

    // ── Instrumented solver wrapper ──────────────────────────────────────────

    private sealed class InstrumentedSolver : IDeckStrategy
    {
        private readonly IDeckStrategy _inner;
        public bool Fired { get; private set; }

        public InstrumentedSolver(IDeckStrategy inner) => _inner = inner;

        public double StrategicScore(GameContext ctx, Player self)
            => _inner.StrategicScore(ctx, self);

        public PriorityAction? TryGetNextWinningAction(GameContext ctx, Player self)
        {
            var a = _inner.TryGetNextWinningAction(ctx, self);
            if (a is not null) Fired = true;
            return a;
        }

        public MulliganDecision? AdviseMulligan(IReadOnlyList<ICard> hand, int mulligansTaken)
            => _inner.AdviseMulligan(hand, mulligansTaken);

        public IReadOnlyList<string> ReferencedCardNames => _inner.ReferencedCardNames;
    }
}
