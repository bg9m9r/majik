using Majik.Bot.Evaluation;
using Majik.Bot.Tuning;
using Majik.Core.CardData;

namespace Majik.Console.Commands;

/// <summary>
/// Offline self-play weight tuner for <see cref="ArchetypeWeights"/>.
///
/// <para>
/// Runs coordinate-ascent hill-climbing on the <see cref="ArchetypeWeights"/>
/// vector, optimising it by playing mirror games of the chosen archetype
/// (heuristic-vs-heuristic by default for speed; switch to --strategy mcts
/// to tune the search eval). Prints the tuned weights as a copy-pasteable
/// C# named-arg record expression.
/// </para>
///
/// <para>
/// <b>Usage:</b>
/// <code>
/// dotnet run --project Majik.Console -- tune-bot-weights &lt;archetype&gt; [options]
///
/// Arguments:
///   &lt;archetype&gt;          Deck archetype name (Burn | Prowess | BorosEnergy |
///                         AzoriusControl | or any catalog archetype). Required.
///
/// Options:
///   --games N            Games per candidate evaluation. Default 8.
///                        Smoke test: 6–10. Real convergence: 50+.
///   --rounds R           Coordinate-ascent rounds. Each round sweeps all 11
///                        weight dimensions. Default 5.
///   --strategy S         "heuristic" (fast, default) or "mcts" (slow).
///   --step V             Initial perturbation step size. Default 0.5.
///   --bad-start          Start from a deliberately bad vector (for smoke
///                        testing that the optimizer climbs).
/// </code>
/// </para>
///
/// <para>
/// <b>Runtime estimate:</b> Each candidate evaluation = <c>games</c> games.
/// Each round = 22 evaluations (2 per dim × 11 dims). Total evaluations =
/// 2 × 11 × <c>rounds</c>. With heuristic strategy and 8 games per eval,
/// expect ~1–5 s per game → ~20 min per round. With mcts expect ~10× longer.
/// For smoke proof: 2 rounds × 8 games = ~176 games → a few minutes.
/// </para>
/// </summary>
public static class TuneBotWeightsCommand
{
    public static readonly string HelpText =
        """
        tune-bot-weights <archetype> [--games N] [--rounds R] [--strategy heuristic|mcts] [--step V] [--bad-start]

          Runs self-play coordinate-ascent on the ArchetypeWeights vector for the
          given deck archetype and prints the tuned weights as copy-pasteable C#.

          <archetype>  Deck archetype (Burn, Prowess, BorosEnergy, AzoriusControl,
                       or any name in BotDeckCatalog).
          --games N    Games per candidate evaluation (default 8; smoke: 6-10;
                       convergence: 50+). Each game is heuristic-vs-heuristic
                       (fast) unless --strategy mcts is passed.
          --rounds R   Coordinate-ascent rounds (default 5). Each round sweeps
                       all 11 weight dimensions (22 candidate evals per round).
          --strategy S "heuristic" (default, fast) or "mcts" (slow, tunes search
                       eval quality rather than pure heuristic eval).
          --step V     Initial perturbation step size (default 0.5). Shrinks by
                       0.8x each round.
          --bad-start  Start from a deliberately bad weight vector (all 0.0 except
                       LifeDelta=0.1) instead of the current archetype weights.
                       Use to smoke-test that the optimizer climbs.

        Example (smoke test — proves optimizer climbs from bad start, ~2-4 min):
          dotnet run --project Majik.Console -- tune-bot-weights Prowess --games 8 --rounds 3 --bad-start

        Example (real tuning pass, hours):
          dotnet run --project Majik.Console -- tune-bot-weights BorosEnergy --games 50 --rounds 10
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        // Parse arguments
        string? archetype = null;
        int games = 8;
        int rounds = 5;
        string strategy = "heuristic";
        double step = 0.5;
        bool badStart = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--games"    when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out games) || games < 1)
                    {
                        System.Console.Error.WriteLine($"error: --games must be a positive integer, got '{args[i]}'");
                        return 1;
                    }
                    break;
                case "--rounds"   when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out rounds) || rounds < 1)
                    {
                        System.Console.Error.WriteLine($"error: --rounds must be a positive integer, got '{args[i]}'");
                        return 1;
                    }
                    break;
                case "--strategy" when i + 1 < args.Length:
                    strategy = args[++i];
                    if (strategy != "heuristic" && strategy != "mcts")
                    {
                        System.Console.Error.WriteLine($"error: --strategy must be 'heuristic' or 'mcts', got '{strategy}'");
                        return 1;
                    }
                    break;
                case "--step"     when i + 1 < args.Length:
                    if (!double.TryParse(args[++i], out step) || step <= 0)
                    {
                        System.Console.Error.WriteLine($"error: --step must be a positive number, got '{args[i]}'");
                        return 1;
                    }
                    break;
                case "--bad-start":
                    badStart = true;
                    break;
                default:
                    if (!args[i].StartsWith("--"))
                        archetype = args[i];
                    else
                    {
                        System.Console.Error.WriteLine($"error: unknown option '{args[i]}'");
                        return 1;
                    }
                    break;
            }
        }

        if (archetype is null)
        {
            System.Console.Error.WriteLine("error: <archetype> argument is required");
            System.Console.Error.WriteLine();
            System.Console.Error.WriteLine(HelpText);
            return 1;
        }

        // Resolve starting weights
        ArchetypeWeights startWeights = badStart
            ? BadStartWeights()
            : ArchetypeWeights.ForArchetype(archetype);

        System.Console.WriteLine($"[TUNE] tune-bot-weights archetype={archetype} games={games} rounds={rounds} strategy={strategy} step={step} bad-start={badStart}");
        System.Console.WriteLine($"[TUNE] loading embedded card repository...");

        var repo = new EmbeddedCardRepository();

        System.Console.WriteLine($"[TUNE] repository loaded. starting tuner.");

        var tuner = new WeightTuner(
            repo:          repo,
            deck:          archetype,
            games:         games,
            maxRounds:     rounds,
            initialStep:   step,
            stepDecay:     0.8,
            acceptMargin:  0.02,
            strategy:      strategy,
            log:           msg => System.Console.WriteLine(msg));

        var tuned = await tuner.TuneWeights(startWeights);

        System.Console.WriteLine();
        System.Console.WriteLine("=== TUNED WEIGHTS (copy-pasteable) ===");
        System.Console.WriteLine(WeightTuner.Format(tuned));
        System.Console.WriteLine();
        System.Console.WriteLine($"Paste the above into ArchetypeWeights.cs as the {archetype} entry,");
        System.Console.WriteLine("or use it as a new named constant for bespoke profiles.");

        return 0;
    }

    /// <summary>
    /// Deliberately bad starting weight vector for smoke testing: the
    /// Prowess production weights with all values scaled to near-zero
    /// (×0.05). Signs are preserved so the bot still makes directionally
    /// correct decisions (it will still attack when ahead on board), but
    /// the weights are so weak that the eval has almost no discrimination
    /// power — the bot plays nearly uniformly. The tuner should quickly
    /// accept perturbations that scale weights toward their productive range.
    ///
    /// <para>
    /// Design rationale: all-zero weights create a completely flat evaluation
    /// landscape; wrong-sign weights make the bot refuse to attack (so games
    /// draw at turn cap, giving a 0.5 signal for every perturbation). A
    /// "scaled-down production" vector preserves the landscape shape but
    /// compresses it, producing decisions that are slightly better than random
    /// and sufficiently different from the full-scale vector that the optimizer
    /// can detect improvements with a modest game budget.
    /// </para>
    /// </summary>
    public static ArchetypeWeights BadStartWeights()
    {
        // Prowess production weights ×0.05 — "ghost" of the right shape.
        var prod = ArchetypeWeights.ForArchetype("Prowess");
        const double scale = 0.05;
        return new ArchetypeWeights(
            LifeDelta:           prod.LifeDelta           * scale,
            BoardPower:          prod.BoardPower          * scale,
            BoardToughness:      prod.BoardToughness      * scale,
            OpponentThreats:     prod.OpponentThreats     * scale,
            ManaSources:         prod.ManaSources         * scale,
            HandSize:            prod.HandSize            * scale,
            Tempo:               prod.Tempo               * scale,
            KeyCardInPlay:       prod.KeyCardInPlay       * scale,
            LethalProximity:     prod.LethalProximity     * scale,
            CardAdvantage:       prod.CardAdvantage       * scale,
            PlaneswalkerEngine:  prod.PlaneswalkerEngine  * scale);
    }
}
