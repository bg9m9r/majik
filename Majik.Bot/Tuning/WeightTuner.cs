using Majik.Bot.Evaluation;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Random;

namespace Majik.Bot.Tuning;

/// <summary>
/// Self-play eval-weight tuner for <see cref="ArchetypeWeights"/>.
///
/// <para>
/// <b>Overview:</b> Coordinate-ascent hill-climber that optimises the
/// <see cref="ArchetypeWeights"/> vector by playing mirror games of a chosen
/// deck (candidate weights bot vs current-best weights bot). The candidate's
/// win-rate is used as the objective. Games are heuristic-vs-heuristic by
/// default (fast; the strategy is the only difference, so weight quality is
/// isolated). MCTS can be selected via <paramref name="strategy"/> for tuning
/// the search eval (slower).
/// </para>
///
/// <para>
/// <b>Objective — <see cref="EvaluateWeights"/>:</b>
/// Plays <c>games</c> mirror games, alternating seats (to cancel first-move
/// advantage), and returns the candidate's score:
/// <c>win-rate + margin-bonus</c>. The margin bonus (average life-delta /
/// ScoreNormalizer) gives a gradient even when all games are decisive, so
/// the optimizer can distinguish "wins by 10 life" from "wins by 1 life" —
/// producing a smoother landscape than pure win/loss.
/// </para>
///
/// <para>
/// <b>Optimizer — <see cref="TuneWeights"/>:</b>
/// Coordinate ascent: for each of the 11 weight dimensions, try +step and
/// −step from the current best. Accept a perturbation when its
/// <see cref="EvaluateWeights"/> score is &gt; 0.5 + <c>margin</c> (candidate
/// strictly beats baseline). Shrink the step size over rounds; stop after
/// <c>maxRounds</c> or when a full round produces no improvement. Progress is
/// logged via <paramref name="log"/> with the prefix <c>[TUNE]</c>.
/// </para>
///
/// <para>
/// <b>Usage:</b> Inject a deliberately bad vector as the start to prove the
/// harness climbs (fast smoke test, a few rounds / 6–10 games). For real
/// convergence use 50+ games and 10+ rounds — each round is ~games × 22
/// engine games (2 perturbations × 11 dims). Expect minutes to hours
/// depending on game count and strategy.
/// </para>
/// </summary>
public sealed class WeightTuner
{
    private readonly ICardRepository _repo;
    private readonly string _deck;
    private readonly int _games;
    private readonly int _maxRounds;
    private readonly double _initialStep;
    private readonly double _stepDecay;
    private readonly double _acceptMargin;
    private readonly string _strategy;
    private readonly Action<string> _log;
    private readonly bool _verbose;

    /// <summary>
    /// Margin bonus normalizer for the life-total differential. Divides the
    /// average per-game life advantage by this constant before adding it to
    /// the win-rate, so the bonus is bounded to ≈ ±1 across typical games
    /// (starting life = 20, so max life advantage ≈ 20 → 20/20 = 1.0).
    /// </summary>
    private const double ScoreNormalizer = 20.0;

    /// <summary>
    /// Initialises a new <see cref="WeightTuner"/>.
    /// </summary>
    /// <param name="repo">Embedded card repository for deck materialisation.</param>
    /// <param name="deck">Archetype name (must be in <see cref="Decks.BotDeckCatalog"/>).</param>
    /// <param name="games">Games per candidate evaluation. 6–10 for smoke; 50+ for real tuning.</param>
    /// <param name="maxRounds">Coordinate-ascent rounds. Each round sweeps all 11 dimensions.</param>
    /// <param name="initialStep">Initial perturbation step size for each weight.</param>
    /// <param name="stepDecay">Multiplicative decay applied to step size each round (e.g. 0.8).</param>
    /// <param name="acceptMargin">Candidate must beat current-best by this margin above 0.5 to be accepted.</param>
    /// <param name="strategy">"heuristic" (fast, default) or "mcts" (slow, tunes search eval).</param>
    /// <param name="log">Sink for [TUNE] progress lines. Defaults to Console.WriteLine.</param>
    /// <param name="verbose">When true, emits [EVAL-DBG] per-game lines. Default false.</param>
    public WeightTuner(
        ICardRepository repo,
        string deck,
        int games = 8,
        int maxRounds = 5,
        double initialStep = 0.5,
        double stepDecay = 0.8,
        double acceptMargin = 0.02,
        string strategy = "heuristic",
        Action<string>? log = null,
        bool verbose = false)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _deck = deck ?? throw new ArgumentNullException(nameof(deck));
        _games = games;
        _maxRounds = maxRounds;
        _initialStep = initialStep;
        _stepDecay = stepDecay;
        _acceptMargin = acceptMargin;
        _strategy = strategy;
        _log = log ?? System.Console.WriteLine;
        _verbose = verbose;
    }

    /// <summary>
    /// Evaluates <paramref name="candidate"/> weights against
    /// <paramref name="baseline"/> weights by playing <c>games</c> mirror games.
    /// Returns a score in [0, ~2]: win-rate + a small margin bonus. Higher is
    /// better for the candidate.
    ///
    /// <para>
    /// Seats are alternated across games to cancel first-move advantage.
    /// Game i: candidate is Alice when i is even, Bob when i is odd.
    /// </para>
    ///
    /// <para>
    /// Inconclusive games (engine exceptions or draws when life totals are
    /// equal) count as 0.5 for both sides (neutral). This is intentional:
    /// a crash / draw is not a loss for the candidate, so the score is not
    /// unfairly penalised.
    /// </para>
    ///
    /// <para>
    /// The margin bonus: after computing win-rate, add the average life-delta
    /// (candidate life − baseline life, clamped to 20) normalized by
    /// <see cref="ScoreNormalizer"/>. This gives a smooth gradient even when
    /// all games are decisive, differentiating "wins by 10" from "wins by 1".
    /// </para>
    /// </summary>
    public async Task<double> EvaluateWeights(
        ArchetypeWeights candidate,
        ArchetypeWeights baseline,
        int baseSeed = 100)
    {
        double scoreSum = 0;
        double marginSum = 0;

        for (int i = 0; i < _games; i++)
        {
            int seed = baseSeed + i;
            bool candidateIsAlice = i % 2 == 0;

            // Build config using WeightsOverride to inject explicit vectors.
            var candidateConfig = new BotConfig(
                _deck,
                Strategy: _strategy,
                RandomSeed: seed,
                WeightsOverride: candidate);

            var baselineConfig = new BotConfig(
                _deck,
                Strategy: _strategy,
                RandomSeed: seed + 500,
                WeightsOverride: baseline);

            string aliceName = candidateIsAlice ? "Candidate" : "Baseline";
            string bobName   = candidateIsAlice ? "Baseline"  : "Candidate";

            var facade = GameFacade.Create(
                aliceName: aliceName,
                bobName:   bobName,
                aliceDeck: LoadDeck(),
                bobDeck:   LoadDeck(),
                cardRepo:  _repo);

            if (candidateIsAlice)
            {
                facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, candidateConfig));
                facade.ReplaceBobAgent(  new BotPlayerAgent(facade.Bob,   baselineConfig));
            }
            else
            {
                facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, baselineConfig));
                facade.ReplaceBobAgent(  new BotPlayerAgent(facade.Bob,   candidateConfig));
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

            double gameScore;
            double gameMargin;
            int    dbgCandidateLife = 20;
            int    dbgBaselineLife  = 20;
            string dbgOutcome       = "inconclusive";

            try
            {
                await facade.StartFullGameAsync(
                    maxTurns: 25,
                    ct: cts.Token,
                    rng: new GameRandom(seed));

                var result = await facade.FullGameTask!;

                // Capture life totals immediately after game ends.
                int candidateLife = candidateIsAlice ? facade.Alice.LifeTotal : facade.Bob.LifeTotal;
                int baselineLife  = candidateIsAlice ? facade.Bob.LifeTotal   : facade.Alice.LifeTotal;

                // Clamp to [0, 20] for the margin calculation.
                int candidateLifeClamp = Math.Clamp(candidateLife, 0, 20);
                int baselineLifeClamp  = Math.Clamp(baselineLife,  0, 20);
                gameMargin = candidateLifeClamp - baselineLifeClamp;

                dbgCandidateLife = candidateLifeClamp;
                dbgBaselineLife  = baselineLifeClamp;

                if (result.Winner == null)
                {
                    // Draw: 0.5 points, margin from life totals.
                    gameScore  = 0.5;
                    dbgOutcome = $"draw turns={result.TurnsPlayed}";
                }
                else
                {
                    // Determine if candidate won.
                    bool candidateWon = candidateIsAlice
                        ? ReferenceEquals(result.Winner, facade.Alice)
                        : ReferenceEquals(result.Winner, facade.Bob);

                    gameScore  = candidateWon ? 1.0 : 0.0;
                    dbgOutcome = candidateWon ? $"candidate-wins turns={result.TurnsPlayed}" : $"baseline-wins turns={result.TurnsPlayed}";
                }
            }
            catch (Exception ex)
            {
                // Inconclusive: count as neutral 0.5, no margin signal.
                gameScore  = 0.5;
                gameMargin = 0.0;
                dbgOutcome = $"error:{ex.GetType().Name}";
            }

            if (_verbose)
            {
                _log($"[EVAL-DBG] game={i} seed={seed} candIsAlice={candidateIsAlice} " +
                     $"outcome={dbgOutcome} " +
                     $"life=cand:{dbgCandidateLife}/base:{dbgBaselineLife} " +
                     $"score={gameScore:F2} margin={gameMargin:+0;-0;0}");
            }

            scoreSum  += gameScore;
            marginSum += gameMargin;
        }

        double winRate     = scoreSum  / _games;
        double avgMargin   = marginSum / _games;
        double finalScore  = winRate + avgMargin / ScoreNormalizer;

        return finalScore;
    }

    /// <summary>
    /// Coordinate-ascent hill-climber. For each of the 11 weight fields in
    /// <see cref="ArchetypeWeights"/>, tries <c>+step</c> and <c>−step</c>
    /// perturbations, evaluating each against the current best via
    /// <see cref="EvaluateWeights"/>. Accepts the perturbation when the
    /// candidate's score beats the CALIBRATED NULL (the self-mirror score of
    /// the current best, see below) by <see cref="_acceptMargin"/>. Shrinks
    /// the step each round. Stops after <see cref="_maxRounds"/> rounds or
    /// when a full round produces no accepted change.
    ///
    /// <para>
    /// <b>Calibrated acceptance null.</b> Heuristic self-play is fully
    /// deterministic per seed (the heuristic strategy ignores
    /// <c>BotConfig.RandomSeed</c>), so a perturbation that does not change
    /// any decision reproduces <c>EvaluateWeights(best, best)</c> EXACTLY —
    /// and that self-mirror score is NOT 0.5 on a finite seed set (seat /
    /// shuffle advantage skews it; e.g. 0.37 on one observed 6-seed block).
    /// Comparing against a fixed 0.5 therefore mis-fires in both directions:
    /// when the self-mirror score is &gt; 0.5 + margin every behaviorally
    /// inert perturbation would be accepted (random drift on dimensions the
    /// heuristic never reads, e.g. CardAdvantage); when it is below, real
    /// improvements get rejected. The tuner instead measures
    /// <c>nullScore = EvaluateWeights(best, best, baseSeed)</c> (recomputed
    /// whenever <c>best</c> changes) and accepts only candidates that beat
    /// THAT by the margin.
    /// </para>
    ///
    /// <para>
    /// The returned vector is the best found; it may differ from
    /// <paramref name="start"/> by more than one dimension (each accepted
    /// perturbation becomes the new best immediately, enabling multi-step
    /// climbs within a round).
    /// </para>
    /// </summary>
    /// <param name="start">Starting weight vector. Use a deliberately bad vector
    /// for smoke testing; use the current production weights for real tuning.</param>
    /// <param name="baseSeed">Base RNG seed for game sequences in each evaluation.</param>
    public async Task<ArchetypeWeights> TuneWeights(
        ArchetypeWeights start,
        int baseSeed = 100)
    {
        var best = start;
        double step = _initialStep;

        _log($"[TUNE] start  step={step:F3} deck={_deck} games={_games} rounds={_maxRounds} strategy={_strategy}");
        _log($"[TUNE] start  weights={Format(best)}");

        // Calibrated acceptance null: the score an exactly-equivalent candidate
        // reproduces on this seed set (see <remarks> — deterministic heuristic
        // self-play makes this the correct zero, not 0.5).
        double nullScore = await EvaluateWeights(best, best, baseSeed);
        _log($"[TUNE] null   self-mirror score={nullScore:F4} (calibrated acceptance null; 0.5 only in expectation)");

        for (int round = 0; round < _maxRounds; round++)
        {
            bool anyAccepted = false;

            foreach (var field in WeightFields())
            {
                // Try +step
                var plusCandidate  = Perturb(best, field, +step);
                double plusScore   = await EvaluateWeights(plusCandidate, best, baseSeed);
                bool plusAccepted  = plusScore > nullScore + _acceptMargin;

                _log($"[TUNE] round={round} field={field,20} delta=+{step:F3} score={plusScore:F4} null={nullScore:F4} accepted={plusAccepted}");

                if (plusAccepted)
                {
                    best = plusCandidate;
                    anyAccepted = true;
                    nullScore = await EvaluateWeights(best, best, baseSeed);
                    continue; // Skip −step: we already improved on this dimension.
                }

                // Try −step
                var minusCandidate = Perturb(best, field, -step);
                double minusScore  = await EvaluateWeights(minusCandidate, best, baseSeed);
                bool minusAccepted = minusScore > nullScore + _acceptMargin;

                _log($"[TUNE] round={round} field={field,20} delta=-{step:F3} score={minusScore:F4} null={nullScore:F4} accepted={minusAccepted}");

                if (minusAccepted)
                {
                    best = minusCandidate;
                    anyAccepted = true;
                    nullScore = await EvaluateWeights(best, best, baseSeed);
                }
            }

            _log($"[TUNE] round={round} end   step={step:F3} anyAccepted={anyAccepted} weights={Format(best)}");

            step *= _stepDecay;

            if (!anyAccepted)
            {
                _log($"[TUNE] round={round} no improvement — stopping early");
                break;
            }
        }

        _log($"[TUNE] done   final weights={Format(best)}");
        return best;
    }

    /// <summary>
    /// A genuinely-bad weight vector for harness proofs (FastProof test,
    /// <c>tune-bot-weights --bad-start</c>): production weights should beat it
    /// decisively in self-play, and the optimizer should climb away from it.
    ///
    /// <para>
    /// <b>Why not "production × 0.05"?</b> The previous bad vector scaled the
    /// production weights uniformly. That is a NO-OP: every heuristic decision
    /// is an argmax over deltas that are LINEAR in the weights (BoardEval,
    /// CombatEval, the cast/ability projections), so a uniformly scaled vector
    /// makes byte-identical decisions — the only scale-variant input is the
    /// vanilla-shell CMC penalty, and the curated decks no longer contain
    /// vanilla shells. Mirror games between production and the scaled vector
    /// were literally the same deterministic games as production-vs-production
    /// (verified game-by-game), so the old "proof" measured pure seat/shuffle
    /// variance.
    /// </para>
    ///
    /// <para>
    /// This vector instead scrambles the RATIOS (scale-variant badness):
    /// <list type="bullet">
    ///   <item><c>HandSize 2.5</c> + <c>Tempo 0.05</c> — hoards: every cast
    ///     costs −2.5; instants/sorceries (delta ≈ −2.5 + 0.05·cmc) are NEVER
    ///     cast — crippling for spell decks.</item>
    ///   <item><c>BoardPower 0.1</c> + <c>BoardToughness 2.0</c> — only
    ///     casts high-toughness bodies; undervalues every threat.</item>
    ///   <item><c>LifeDelta 0.2</c> + <c>LethalProximity 0.1</c> — barely
    ///     races; attacks only when nearly free (keeps games game-ending:
    ///     the PRODUCTION side still closes every game well before the turn
    ///     cap, so the objective gets decisive signal instead of draws).</item>
    ///   <item><c>OpponentThreats +0.5</c> — wrong sign: removal abilities
    ///     project NEGATIVE, so it never removes threats.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static ArchetypeWeights DegenerateWeights() => new(
        LifeDelta:           0.2,
        BoardPower:          0.1,
        BoardToughness:      2.0,
        OpponentThreats:     0.5,   // wrong sign — likes opposing threats
        ManaSources:         0.1,
        HandSize:            2.5,   // hoarder: refuses to spend cards
        Tempo:               0.05,  // never casts instants/sorceries
        KeyCardInPlay:       0.0,
        LethalProximity:     0.1,
        CardAdvantage:       0.0,
        PlaneswalkerEngine:  0.0);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IReadOnlyList<Majik.Core.Cards.ICard> LoadDeck()
    {
        var names = Decks.BotDeckCatalog.Get(_deck);
        return names.Select(n => LoadCard(n)).ToList();
    }

    // Delegates to the shared DeckCardShellBuilder so the tuner's self-play
    // decks materialize identically to the server's RealDeckLoader —
    // preserving all printed card types (CR 205.1b) and the color indicator
    // (CR 202.2c). GameFacade rebinds abilities during self-play.
    private Majik.Core.Cards.ICard LoadCard(string name)
    {
        var entity = _repo.GetByName(name)
            ?? throw new InvalidOperationException($"WeightTuner: card not in seed: '{name}'");

        return Majik.Core.CardData.DeckCardShellBuilder.Build(entity);
    }

    /// <summary>
    /// Returns the names of the 11 weight fields in a fixed order that covers
    /// all dimensions of <see cref="ArchetypeWeights"/>. Order is stable across
    /// runs so progress logs are reproducible.
    /// </summary>
    private static IEnumerable<string> WeightFields() =>
    [
        nameof(ArchetypeWeights.LifeDelta),
        nameof(ArchetypeWeights.BoardPower),
        nameof(ArchetypeWeights.BoardToughness),
        nameof(ArchetypeWeights.OpponentThreats),
        nameof(ArchetypeWeights.ManaSources),
        nameof(ArchetypeWeights.HandSize),
        nameof(ArchetypeWeights.Tempo),
        nameof(ArchetypeWeights.KeyCardInPlay),
        nameof(ArchetypeWeights.LethalProximity),
        nameof(ArchetypeWeights.CardAdvantage),
        nameof(ArchetypeWeights.PlaneswalkerEngine),
    ];

    /// <summary>
    /// Returns a copy of <paramref name="w"/> with the field identified by
    /// <paramref name="field"/> perturbed by <paramref name="delta"/>.
    /// Negative weights are allowed (e.g. <c>OpponentThreats</c> is already
    /// negative in the default tables).
    /// </summary>
    private static ArchetypeWeights Perturb(ArchetypeWeights w, string field, double delta) =>
        field switch
        {
            nameof(ArchetypeWeights.LifeDelta)          => w with { LifeDelta          = w.LifeDelta          + delta },
            nameof(ArchetypeWeights.BoardPower)         => w with { BoardPower         = w.BoardPower         + delta },
            nameof(ArchetypeWeights.BoardToughness)     => w with { BoardToughness     = w.BoardToughness     + delta },
            nameof(ArchetypeWeights.OpponentThreats)    => w with { OpponentThreats    = w.OpponentThreats    + delta },
            nameof(ArchetypeWeights.ManaSources)        => w with { ManaSources        = w.ManaSources        + delta },
            nameof(ArchetypeWeights.HandSize)           => w with { HandSize           = w.HandSize           + delta },
            nameof(ArchetypeWeights.Tempo)              => w with { Tempo              = w.Tempo              + delta },
            nameof(ArchetypeWeights.KeyCardInPlay)      => w with { KeyCardInPlay      = w.KeyCardInPlay      + delta },
            nameof(ArchetypeWeights.LethalProximity)    => w with { LethalProximity    = w.LethalProximity    + delta },
            nameof(ArchetypeWeights.CardAdvantage)      => w with { CardAdvantage      = w.CardAdvantage      + delta },
            nameof(ArchetypeWeights.PlaneswalkerEngine) => w with { PlaneswalkerEngine = w.PlaneswalkerEngine + delta },
            _ => throw new ArgumentException($"Unknown weight field: {field}", nameof(field)),
        };

    /// <summary>
    /// Formats a weight vector as a copy-pasteable C# named-arg constructor
    /// call, so the operator can paste the tuned result directly into
    /// <see cref="ArchetypeWeights"/>.
    /// </summary>
    public static string Format(ArchetypeWeights w) =>
        $"new ArchetypeWeights(" +
        $"LifeDelta:{w.LifeDelta:F3}, " +
        $"BoardPower:{w.BoardPower:F3}, " +
        $"BoardToughness:{w.BoardToughness:F3}, " +
        $"OpponentThreats:{w.OpponentThreats:F3}, " +
        $"ManaSources:{w.ManaSources:F3}, " +
        $"HandSize:{w.HandSize:F3}, " +
        $"Tempo:{w.Tempo:F3}, " +
        $"KeyCardInPlay:{w.KeyCardInPlay:F3}, " +
        $"LethalProximity:{w.LethalProximity:F3}, " +
        $"CardAdvantage:{w.CardAdvantage:F3}, " +
        $"PlaneswalkerEngine:{w.PlaneswalkerEngine:F3})";
}
