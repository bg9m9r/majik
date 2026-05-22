using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;

namespace Majik.Bot.Heuristic;

/// <summary>
/// Orders a single controller's batch of simultaneously-fired triggered
/// abilities (CR 603.3b). The active player chooses the order; race lines
/// and ETB loops are sensitive to which trigger resolves first.
///
/// Style mirrors <see cref="ModalPolicy"/>: a lightweight bag-of-keywords
/// scorer over each trigger's effect-description text. No archetype awareness
/// in v1 — the rank is "more impact = resolve first" across the board.
///
/// Stack semantics reminder: <see cref="Majik.Core.Abilities.TriggerManager"/>
/// pushes the returned list in order, so the FIRST item ends up at the
/// BOTTOM of the stack and resolves LAST. To make the highest-scoring
/// trigger resolve first, it must be the LAST element of the returned list.
/// We sort ascending by score so the best one ends up on top of the stack.
/// </summary>
public static class TriggerOrderPolicy
{
    /// <summary>
    /// Stable-sort <paramref name="mine"/> so high-impact triggers resolve
    /// first. Ties preserve the original order (which carries timestamp
    /// semantics from <see cref="Majik.Core.Abilities.TriggerManager"/>).
    /// </summary>
    public static IReadOnlyList<ITriggeredAbility> Order(
        GameContext ctx,
        IReadOnlyList<ITriggeredAbility> mine)
    {
        if (mine.Count <= 1) return mine;

        // Pair each trigger with its score and its original index for
        // stable ordering, then sort ascending so the best score lands at
        // the END of the list (= top of the stack = resolves first).
        var scored = mine
            .Select((t, idx) => (trigger: t, score: ScoreTrigger(t), idx))
            .OrderBy(x => x.score)
            .ThenBy(x => x.idx)
            .Select(x => x.trigger)
            .ToList();

        return scored;
    }

    /// <summary>
    /// Score a single trigger by inspecting its effects' descriptions and
    /// its source card's text (when available). Higher = more impactful.
    /// </summary>
    public static double ScoreTrigger(ITriggeredAbility trig)
    {
        double s = 0.0;

        // Effects describe what the trigger actually does on resolution.
        if (trig is TriggeredAbility ta)
        {
            foreach (var eff in ta.Effects)
            {
                s += ScoreEffectText(eff?.Description);
            }
        }

        // Fallback / supplement: source-card name. Many engine-internal
        // effects carry sparse Description strings, so the source's
        // printed name is sometimes the only readable signal we have.
        if (trig.Source is ICard card)
        {
            s += 0.5 * ScoreEffectText(card.Name);
        }

        return s;
    }

    /// <summary>
    /// Bag-of-keywords scorer. Boosts damage/draw/counter/token verbs
    /// (race-relevant, tempo-positive), penalises self-cost drawback clauses
    /// (discard / sacrifice / lose life). Mirrors <see cref="ModalPolicy"/>'s
    /// weights so trigger ordering and mode picking agree on what "good"
    /// looks like.
    /// </summary>
    public static double ScoreEffectText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0.0;
        var t = text.ToLowerInvariant();
        double s = 0.0;

        // High-impact: damage / removal / draw / tutor / counter placement /
        // token creation. Damage scores first because race tempo dominates
        // most multi-trigger ETB chains.
        if (t.Contains("deal") && t.Contains("damage")) s += 4.0;
        if (t.Contains("damage to")) s += 1.0;            // catches "X damage to target"
        if (t.Contains("destroy")) s += 3.0;
        if (t.Contains("exile")) s += 3.0;
        if (t.Contains("counter target")) s += 2.5;
        if (t.Contains("draw")) s += 3.0;                 // card advantage = high
        if (t.Contains("search") && t.Contains("library")) s += 2.5;
        if (t.Contains("+1/+1") || t.Contains("counter")) s += 2.0;
        if (t.Contains("create") && t.Contains("token")) s += 2.5;
        if (t.Contains("return") && t.Contains("hand")) s += 1.5;
        if (t.Contains("gain") && t.Contains("life")) s += 1.0;
        if (t.Contains("untap")) s += 1.0;
        if (t.Contains("scry") || t.Contains("surveil")) s += 0.5;

        // Drawback / self-cost clauses → push toward bottom of stack
        // (resolves later, after the upside).
        if (t.Contains("you lose") && t.Contains("life")) s -= 2.0;
        if (t.Contains("sacrifice")) s -= 1.5;
        if (t.Contains("discard")) s -= 1.0;
        if (t.Contains("mill")) s -= 0.5;                 // self-mill is mild drawback

        // Breadth scaler.
        if (t.Contains("each opponent") || t.Contains("each creature")) s += 1.0;

        return s;
    }
}
