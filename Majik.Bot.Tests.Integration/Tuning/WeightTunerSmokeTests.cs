using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Tuning;
using Majik.Core.CardData;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Tuning;

/// <summary>
/// Fast proof-of-concept smoke tests for <see cref="WeightTuner"/>.
///
/// <para>
/// These tests validate that the self-play weight tuning harness WORKS and
/// IMPROVES, without running to convergence (which would take hours). The
/// smoke proof uses:
/// <list type="bullet">
///   <item>A deliberately bad starting vector
///     (<see cref="WeightTuner.DegenerateWeights"/> — scrambled ratios:
///     hoards cards, never casts instants/sorceries, barely races).
///     This is clearly sub-optimal for any MTG archetype.</item>
///   <item>A small game count (6-8 games per eval) for speed.</item>
///   <item>Heuristic-vs-heuristic strategy (fast: no MCTS overhead).</item>
///   <item>Few rounds (2-3): enough to climb out of the bad start without
///     waiting for convergence.</item>
/// </list>
/// The key assertion: <c>tuned weights beat the bad start</c>, i.e. the
/// tuned bot's win-rate vs the bad-start bot is &gt; 0.5. This proves the
/// optimizer climbs.
/// </para>
///
/// <para>
/// <b>NOT a CI gate</b> — all tests here are <c>Skip</c>ped to avoid adding
/// multi-minute runtime to CI. Un-skip and run locally to:
/// <list type="bullet">
///   <item>Verify the harness works after engine changes.</item>
///   <item>Observe [TUNE] progress lines (logged via ITestOutputHelper).</item>
///   <item>Confirm the optimizer climbs from a known-bad start.</item>
/// </list>
/// Full per-archetype convergence is a separate offline job (see
/// <c>tune-bot-weights</c> Console subcommand).
/// </para>
/// </summary>
public sealed class WeightTunerSmokeTests
{
    private readonly ITestOutputHelper _out;

    /// <summary>
    /// Shared card repository (loaded once, ~22k rows from embedded gz resource).
    /// </summary>
    private static readonly EmbeddedCardRepository Repo = new();

    public WeightTunerSmokeTests(ITestOutputHelper output)
    {
        _out = output;
    }

    /// <summary>
    /// Fast smoke: starts from a deliberately bad weight vector and runs a
    /// few rounds of coordinate ascent with 8 games per eval (heuristic).
    ///
    /// <para>
    /// Proves the harness climbs: the tuned weights must beat the bad start
    /// when they play each other (win-rate of tuned-vs-bad-start &gt; 0.5,
    /// equivalent to <see cref="WeightTuner.EvaluateWeights"/> score &gt; 0.5).
    /// </para>
    ///
    /// <para>
    /// Runtime: ~2-5 minutes on heuristic strategy with 8 games per eval and
    /// 3 rounds (3 × 22 × 8 ≈ 528 games, each a fast heuristic match).
    /// </para>
    /// </summary>
    [Fact(Skip =
        "On-demand smoke test for the weight tuner — not a CI gate. " +
        "Run manually to verify the harness climbs from a bad start. " +
        "Expected: tuned weights beat bad-start with win-rate > 0.5. " +
        "Runtime: ~2-5 min (heuristic, 8 games/eval, 3 rounds).")]
    public async Task TuneWeights_ClimbsFromBadStart_TunedBeatsStart()
    {
        const string deck       = "Prowess";
        const int    games      = 8;
        const int    rounds     = 3;
        const double step       = 0.5;

        // Deliberately bad starting vector: scrambled ratios (hoards cards,
        // never casts instants/sorceries, barely races). NOTE: must be
        // scale-VARIANT bad — an earlier version used "production × 0.05",
        // which is decision-equivalent to production (heuristic decisions are
        // argmax over weight-linear deltas) and gave the optimizer nothing to
        // climb. All-zero / fully-wrong-sign vectors are equally useless:
        // neither bot attacks and every game draws at the turn cap.
        var badStart = WeightTuner.DegenerateWeights();

        _out.WriteLine($"[SMOKE] Starting from bad weight vector: {WeightTuner.Format(badStart)}");

        var tuner = new WeightTuner(
            repo:         Repo,
            deck:         deck,
            games:        games,
            maxRounds:    rounds,
            initialStep:  step,
            stepDecay:    0.8,
            acceptMargin: 0.02,
            strategy:     "heuristic",
            log:          msg => _out.WriteLine(msg));

        var tuned = await tuner.TuneWeights(badStart, baseSeed: 9001);

        _out.WriteLine($"[SMOKE] Tuned vector: {WeightTuner.Format(tuned)}");

        // Evaluate tuned vs bad-start to confirm the optimizer climbed.
        // seed 8000 is distinct from the tuning seed (9001) so we're not
        // just re-measuring training games.
        const double verificationGames = 10;
        var verifier = new WeightTuner(
            repo:         Repo,
            deck:         deck,
            games:        (int)verificationGames,
            maxRounds:    1,
            strategy:     "heuristic",
            log:          msg => _out.WriteLine(msg));

        double tunedVsBadScore = await verifier.EvaluateWeights(tuned, badStart, baseSeed: 8000);

        _out.WriteLine($"[SMOKE] EvaluateWeights(tuned vs bad-start) = {tunedVsBadScore:F4} (threshold > 0.5)");

        tunedVsBadScore.Should().BeGreaterThan(0.5,
            because:
                $"the tuned weights (after {rounds} rounds from bad start) must beat " +
                $"the bad-start vector in a self-play evaluation. " +
                $"Score={tunedVsBadScore:F4}. Tuned={WeightTuner.Format(tuned)}. " +
                $"Bad start={WeightTuner.Format(badStart)}.");
    }

    /// <summary>
    /// Verifies that <see cref="WeightTuner.EvaluateWeights"/> returns a
    /// score &gt; 0.5 when comparing the production Prowess weights against
    /// the deliberately bad start vector (without any tuning). This is a
    /// faster sanity check: if production weights lose to the bad start,
    /// the eval objective itself is broken.
    ///
    /// <para>
    /// Runtime: ~30-90 s (8 games, heuristic strategy).
    /// </para>
    /// </summary>
    [Fact(Skip =
        "On-demand eval objective sanity check — not a CI gate. " +
        "Verifies production weights beat bad-start in EvaluateWeights. " +
        "If this fails, the objective function is inverted or broken. " +
        "Runtime: ~30-90 s (8 games, heuristic).")]
    public async Task EvaluateWeights_ProductionBeatsGarbageWeights()
    {
        const string deck  = "Prowess";
        const int    games = 8;

        var production = ArchetypeWeights.ForArchetype(deck);
        // Scrambled-ratio garbage (see WeightTuner.DegenerateWeights for why
        // a uniformly scaled-down production vector is NOT usable here: it is
        // decision-equivalent to production under the argmax heuristic).
        var garbage = WeightTuner.DegenerateWeights();

        _out.WriteLine($"[EVAL] Production: {WeightTuner.Format(production)}");
        _out.WriteLine($"[EVAL] Garbage:    {WeightTuner.Format(garbage)}");

        var tuner = new WeightTuner(
            repo:      Repo,
            deck:      deck,
            games:     games,
            maxRounds: 1,
            strategy:  "heuristic",
            log:       msg => _out.WriteLine(msg));

        double score = await tuner.EvaluateWeights(production, garbage, baseSeed: 7000);

        _out.WriteLine($"[EVAL] EvaluateWeights(production vs garbage) = {score:F4} (expected > 0.5)");

        score.Should().BeGreaterThan(0.5,
            because:
                $"production Prowess weights must beat the garbage vector in self-play " +
                $"(if they don't, the eval objective is inverted or the game engine is " +
                $"returning incorrect results). Score={score:F4}.");
    }
}
