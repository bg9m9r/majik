using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Players.Agents;

/// <summary>
/// Describes what an effect needs targeted: cardinality + legal candidates.
/// Candidates may be cards (creatures, permanents) or players; both fit
/// the `object` slot — engine validates per the source spell's rules.
/// </summary>
public sealed record TargetRequest(
    string Description,
    int MinTargets,
    int MaxTargets,
    IReadOnlyList<object> LegalCandidates);
