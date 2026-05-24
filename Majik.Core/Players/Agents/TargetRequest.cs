using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.Players.Agents;

/// <summary>
/// Describes what an effect needs targeted: cardinality + legal candidates.
/// Candidates may be cards (creatures, permanents) or players; both fit
/// the `object` slot — engine validates per the source spell's rules.
///
/// <para>
/// <see cref="LegalCandidates"/> is the static candidate pool snapshotted at
/// request construction. For dynamic candidate enumeration (e.g. "creatures
/// an opponent controls AT RESOLUTION time"), supply a
/// <see cref="CandidateGatherer"/> instead — the agent-prompt pipeline will
/// invoke it with the live <see cref="GameContext"/> just before prompting
/// and merge the result into <see cref="LegalCandidates"/>.
/// </para>
/// </summary>
public sealed record TargetRequest(
    string Description,
    int MinTargets,
    int MaxTargets,
    IReadOnlyList<object> LegalCandidates,
    BotIntent Intent = BotIntent.None,
    Func<GameContext, IReadOnlyList<object>>? CandidateGatherer = null)
{
    /// <summary>
    /// Materialize the live candidate pool for this request. Returns the
    /// union of <see cref="LegalCandidates"/> and any objects produced by
    /// <see cref="CandidateGatherer"/> (deduped by reference). When neither
    /// source yields anything, returns <see cref="LegalCandidates"/> as-is.
    /// </summary>
    public IReadOnlyList<object> ResolveCandidates(GameContext ctx)
    {
        if (CandidateGatherer == null) return LegalCandidates;
        var gathered = CandidateGatherer(ctx);
        if (gathered == null || gathered.Count == 0) return LegalCandidates;
        if (LegalCandidates.Count == 0) return gathered;
        // Union dedupe by reference.
        var merged = new List<object>(LegalCandidates);
        foreach (var g in gathered)
        {
            if (!merged.Any(m => ReferenceEquals(m, g))) merged.Add(g);
        }
        return merged;
    }

    /// <summary>
    /// Return a copy of this request with <see cref="LegalCandidates"/>
    /// replaced by the supplied list. Used by the engine-side prompt
    /// pipeline to substitute gathered candidates before calling
    /// <see cref="IPlayerAgent.ChooseTargetsAsync"/>.
    /// </summary>
    public TargetRequest WithCandidates(IReadOnlyList<object> candidates) =>
        this with { LegalCandidates = candidates };
}
