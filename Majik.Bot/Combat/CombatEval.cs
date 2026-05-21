using Majik.Bot.Evaluation;

namespace Majik.Bot.Combat;

/// <summary>
/// Leaf scorer for combat search. Aggregates: opponent life lost (+),
/// bot life lost (-), opponent creatures killed (sum P+T) (+), bot
/// creatures killed (sum P+T) (-). Weighted by ArchetypeWeights.
/// </summary>
public static class CombatEval
{
    public static double Score(
        int botLifeLost,
        int oppLifeLost,
        int botCreaturesKilledPowerSum,
        int oppCreaturesKilledPowerSum,
        ArchetypeWeights weights)
    {
        return
              weights.LifeDelta * oppLifeLost
            - weights.LifeDelta * botLifeLost
            + weights.BoardPower * oppCreaturesKilledPowerSum
            - weights.BoardPower * botCreaturesKilledPowerSum;
    }
}
