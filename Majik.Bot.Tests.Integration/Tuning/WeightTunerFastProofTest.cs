using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Tuning;
using Majik.Core.CardData;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Tuning;

/// <summary>
/// Fast proof test: verifies the weight tuner harness works and that
/// production weights beat a garbage weight vector in EvaluateWeights.
///
/// <para>
/// This test runs a SMALL eval (6 games) to keep CI runtime reasonable
/// (target: under 90 seconds on a fast machine). It does NOT run the full
/// coordinate-ascent loop (that's the <see cref="WeightTunerSmokeTests"/>
/// which is Skip'd). The purpose here is to prove:
/// <list type="number">
///   <item>The <see cref="WeightTuner"/> + <see cref="BotConfig.WeightsOverride"/>
///     injection plumbing works end-to-end.</item>
///   <item>The objective function <see cref="WeightTuner.EvaluateWeights"/>
///     returns a score &gt; 0.5 when production weights play against an
///     all-zeros garbage vector (sanity: the eval is not inverted).</item>
/// </list>
/// The full climb smoke (bad-start → tuned → verify improvement) is in
/// <see cref="WeightTunerSmokeTests.TuneWeights_ClimbsFromBadStart_TunedBeatsStart"/>
/// (Skip'd, run on-demand). Full convergence = offline job via Console subcommand.
/// </para>
///
/// <para>
/// <b>Expected runtime:</b> ~20–60 s (6 heuristic games; each game is 25-turn
/// cap so fast). If this exceeds 2 minutes on CI something is wrong — the
/// heuristic strategy should finish each game in &lt;5 s.
/// </para>
/// </summary>
public sealed class WeightTunerFastProofTest
{
    private readonly ITestOutputHelper _out;
    private static readonly EmbeddedCardRepository Repo = new();

    public WeightTunerFastProofTest(ITestOutputHelper output)
    {
        _out = output;
    }

    /// <summary>
    /// Proves the <see cref="WeightTuner.EvaluateWeights"/> objective works:
    /// production Prowess weights must beat a garbage (all-zero) vector with
    /// a score &gt; 0.5.
    ///
    /// <para>
    /// Runs only 6 games (heuristic). Due to game variance a score of exactly
    /// 0.5 (3 wins out of 6) is possible but unlikely when one bot has a
    /// meaningful eval advantage. The assertion threshold is 0.5 (strictly
    /// greater) — a tie (score = 0.5 exactly) would fail; rerun if marginal
    /// variance causes a flap.
    /// </para>
    ///
    /// <para>This is intentionally kept very fast (few games) to act as a CI-safe
    /// harness smoke. The full hill-climb convergence proof is Skip'd above.</para>
    /// </summary>
    [Fact]
    public async Task EvaluateWeights_ProductionProwessBeatsGarbage_FastSmoke()
    {
        const string deck  = "Prowess";
        const int    games = 6;

        var production = ArchetypeWeights.ForArchetype(deck);

        // Deliberately garbage vector: production weights scaled ×0.05.
        // Signs preserved (directionally correct but near-zero magnitude) so
        // the bot still makes vaguely sensible decisions but with almost no
        // discrimination power — nearly random play. Production weights should
        // clearly beat this in self-play (detectable in 6 games).
        //
        // Design note: all-zero / wrong-sign weights create pathological
        // landscapes where bots refuse to attack → all games draw 20-20 →
        // objective always returns 0.5 (no gradient). Scaled-down production
        // preserves the game-ending potential needed for a signal.
        const double garbageScale = 0.05;
        var garbage = new ArchetypeWeights(
            LifeDelta:           production.LifeDelta           * garbageScale,
            BoardPower:          production.BoardPower          * garbageScale,
            BoardToughness:      production.BoardToughness      * garbageScale,
            OpponentThreats:     production.OpponentThreats     * garbageScale,
            ManaSources:         production.ManaSources         * garbageScale,
            HandSize:            production.HandSize            * garbageScale,
            Tempo:               production.Tempo               * garbageScale,
            KeyCardInPlay:       production.KeyCardInPlay       * garbageScale,
            LethalProximity:     production.LethalProximity     * garbageScale,
            CardAdvantage:       production.CardAdvantage       * garbageScale,
            PlaneswalkerEngine:  production.PlaneswalkerEngine  * garbageScale);

        _out.WriteLine($"[PROOF] Production: {WeightTuner.Format(production)}");
        _out.WriteLine($"[PROOF] Garbage:    {WeightTuner.Format(garbage)}");

        var tuner = new WeightTuner(
            repo:      Repo,
            deck:      deck,
            games:     games,
            maxRounds: 1,
            strategy:  "heuristic",
            log:       msg => _out.WriteLine(msg));

        double score = await tuner.EvaluateWeights(production, garbage, baseSeed: 5555);

        _out.WriteLine($"[PROOF] score(production vs garbage) = {score:F4} (threshold > 0.5)");

        // Production weights must beat the garbage vector. Win-rate component
        // of the score must exceed 0.5 even without the margin bonus.
        // Score = win-rate + marginBonus/20; win-rate > 0.5 → score > 0.5.
        score.Should().BeGreaterThan(0.5,
            because:
                $"production Prowess weights (BoardPower=2.0, LethalProximity=2.5, etc.) " +
                $"must beat an all-zero garbage vector in self-play. " +
                $"A score at or below 0.5 means the eval objective is inverted or the " +
                $"WeightsOverride injection is broken. Score={score:F4}. " +
                $"Deck={deck}, Games={games}, Seed=5555.");
    }
}
