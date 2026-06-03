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
///
/// <para>
/// <see cref="PrintedMinTargets"/> (default = <see cref="MinTargets"/>) is the
/// PRINTED minimum a chosen mode demands (CR 601.2c). For a modal spell whose
/// per-mode requests carry <see cref="MinTargets"/> = 0 so UNCHOSEN modes don't
/// gate the cast, this field records the minimum that mode actually requires
/// once it IS chosen ("Target creature gets −2/−2" → 1). The modal
/// target-collection path in <c>SpellCastFlow</c> raises a chosen mode's
/// effective minimum to this value via <see cref="AsChosenMode"/>, so an
/// escalate-paid (or single-mode) targeted mode with no legal target makes the
/// whole cast illegal and rewinds (CR 601.2c), rather than no-opping on
/// resolution. For non-modal requests it equals <see cref="MinTargets"/> and is
/// inert.
/// </para>
///
/// <para>
/// <see cref="ModeIndex"/> (default = null) ties this request to a specific
/// printed mode of a modal spell (CR 700.2d). It is only needed for SPARSE
/// modal spells whose targeted modes don't line up one-request-per-mode with
/// the printed mode list — e.g. Cryptic Command ("Choose two —") prints four
/// modes but only two are targeted (mode 0 = counter, mode 1 = bounce). When
/// set, the modal target-collection path in <c>SpellCastFlow</c> keys the
/// request to its mode index: it collects targets only when that mode was
/// chosen (raising the effective minimum to <see cref="EffectiveChosenMinTargets"/>
/// so a chosen targeted mode with no legal target rewinds the cast per
/// CR 601.2c), and returns the slot at <c>Targets[ModeIndex]</c> so the
/// EffectFactory's per-mode index lookups stay aligned. Aligned modal spells
/// (one request per mode, e.g. the Charm family) leave this null and rely on
/// positional alignment.
/// </para>
/// </summary>
public sealed record TargetRequest(
    string Description,
    int MinTargets,
    int MaxTargets,
    IReadOnlyList<object> LegalCandidates,
    BotIntent Intent = BotIntent.None,
    Func<GameContext, IReadOnlyList<object>>? CandidateGatherer = null,
    int? PrintedMinTargets = null,
    int? ModeIndex = null)
{
    /// <summary>
    /// CR 601.2c — the PRINTED minimum this request demands when its mode is
    /// chosen. Defaults to <see cref="MinTargets"/> when not explicitly set,
    /// so the existing non-modal target requests are unaffected.
    /// </summary>
    public int EffectiveChosenMinTargets => PrintedMinTargets ?? MinTargets;

    /// <summary>
    /// CR 601.2c — return a copy of this request with <see cref="MinTargets"/>
    /// raised to <see cref="EffectiveChosenMinTargets"/>, used by the modal
    /// target-collection path to enforce the printed minimum of a CHOSEN mode.
    /// When the printed minimum is not greater than the current minimum the
    /// request is returned unchanged.
    /// </summary>
    public TargetRequest AsChosenMode() =>
        EffectiveChosenMinTargets > MinTargets
            ? this with { MinTargets = EffectiveChosenMinTargets }
            : this;
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
