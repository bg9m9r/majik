
namespace Majik.Bot.Frozen.FB1;

/// <summary>
/// Leaf scorer for combat search. Aggregates: opponent life lost (+),
/// bot life lost (-), opponent creatures killed (sum P+T) (+), bot
/// creatures killed (sum P+T) (-). Weighted by ArchetypeWeights.
///
/// <para>
/// The lethal-proximity term rewards damage that brings the opponent close
/// to zero. Specifically, the marginal value of each damage point rises
/// steeply when the opponent's resulting life-total would drop below the
/// <see cref="BoardEval.LowLifeThreshold"/>. This is consistent with
/// <see cref="BoardEval.LethalProximityBonus"/> so both the board-state
/// eval and the combat leaf eval point the bot toward closing games.
/// </para>
/// </summary>
internal static class CombatEval
{
    /// <summary>
    /// Score a combat outcome from the bot's perspective.
    /// </summary>
    /// <param name="botLifeLost">Life the bot loses in this combat.</param>
    /// <param name="oppLifeLost">Life the opponent loses in this combat.</param>
    /// <param name="botCreaturesKilledPowerSum">Sum of (P+T) of bot creatures killed.</param>
    /// <param name="oppCreaturesKilledPowerSum">Sum of (P+T) of opp creatures killed.</param>
    /// <param name="weights">Archetype-specific weights.</param>
    /// <param name="oppLifeBefore">
    /// Opponent's life total BEFORE this combat. Used to compute the
    /// lethal-proximity ramp: the closer the opponent's resulting life is to
    /// zero, the higher the bonus. Defaults to 20 (safe fallback for
    /// callers that do not have access to the live board state).
    /// </param>
    public static double Score(
        int botLifeLost,
        int oppLifeLost,
        int botCreaturesKilledPowerSum,
        int oppCreaturesKilledPowerSum,
        ArchetypeWeights weights,
        int oppLifeBefore = 20)
    {
        // Lethal-proximity bonus: how much better is the opponent's life AFTER
        // this attack vs before? The delta in LethalProximityBonus captures the
        // non-linear ramp (the bonus is convex — it grows faster near 0).
        var oppLifeAfter = Math.Max(0, oppLifeBefore - oppLifeLost);
        var proximityDelta =
            BoardEval.LethalProximityBonus(oppLifeAfter)
            - BoardEval.LethalProximityBonus(oppLifeBefore);

        return
              weights.LifeDelta       * oppLifeLost
            - weights.LifeDelta       * botLifeLost
            + weights.BoardPower      * oppCreaturesKilledPowerSum
            - weights.BoardPower      * botCreaturesKilledPowerSum
            + weights.LethalProximity * proximityDelta;
    }
}
