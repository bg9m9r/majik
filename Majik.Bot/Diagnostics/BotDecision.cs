namespace Majik.Bot.Diagnostics;

/// <summary>
/// Structured record describing a single bot decision. Used by
/// <see cref="IBotDecisionSink"/> implementations to surface "why bot did X"
/// during manual testing and (future) replay tooling.
///
/// <para>Deliberately decision-level: no full game state, no PII, no card
/// content beyond names already visible to both players. The intent is to
/// make EV comparisons inspectable, not to reconstruct game state.</para>
/// </summary>
/// <param name="DecisionType">Short tag identifying the policy that emitted
/// the decision, e.g. <c>"Priority"</c>, <c>"Combat.Attackers"</c>,
/// <c>"ActivatedAbility"</c>. Stable string — log scrapers can filter on it.</param>
/// <param name="Chosen">Human-readable label for the picked action, e.g.
/// <c>"CastSpell:Lightning Bolt"</c> or <c>"Attack with {Goblin Guide, Bear}"</c>.</param>
/// <param name="ChosenScore">EV / projected-eval score the policy assigned
/// to the chosen action. Same scale as the rest of the policy.</param>
/// <param name="Alternatives">Up to ~3 losing candidates with their scores,
/// already sorted descending by score. Empty when the chosen action was the
/// only candidate (e.g. forced Pass).</param>
/// <param name="Context">Free-form flag bag — mana-screw, board-behind,
/// life-low, etc. Keys are stable strings, values are stringified scalars.</param>
public sealed record BotDecision(
    string DecisionType,
    string Chosen,
    double ChosenScore,
    IReadOnlyList<BotDecisionAlternative> Alternatives,
    IReadOnlyDictionary<string, string> Context);

/// <summary>A single losing candidate with the score it received.</summary>
public sealed record BotDecisionAlternative(string Name, double Score);
