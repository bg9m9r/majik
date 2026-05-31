using Majik.Core.Cards;
using Majik.Core.Game;

namespace Majik.Core.Players.Agents;

/// <summary>
/// PLAN 01 (Slice C) — the kind of declarative choice a
/// <see cref="ChoiceRequest"/> represents. The single
/// <see cref="IPlayerAgent.ChooseAsync"/> sink switches on this to surface
/// the right UI / bot policy. Extend as new declarative choices are folded
/// in (the per-prompt bespoke methods remain as shims for now — Slice G
/// deletes them once every caller routes through <see cref="ChooseAsync"/>).
/// </summary>
public enum ChoiceKind
{
    /// <summary>Optional "may" gate — pick 0 or 1 "yes" sentinel (CR 117.x / 605.1).</summary>
    YesNo,

    /// <summary>Pick exactly one candidate (or decline when optional).</summary>
    PickOne,

    /// <summary>Pick N (Min..Max) candidates.</summary>
    PickN,

    /// <summary>Order the candidates (e.g. trigger ordering, CR 603.3b).</summary>
    Order,

    /// <summary>CR 701.20 — Scry partition (top-order vs. bottom).</summary>
    ScryPartition,

    /// <summary>CR 701.42 — Surveil partition (top-order vs. graveyard).</summary>
    SurveilPartition,
}

/// <summary>
/// PLAN 01 (Slice C) — declarative description of a non-targeting player
/// choice, mirroring <see cref="TargetRequest"/>. Replaces the ~20 bespoke
/// <c>IPlayerAgent.ChooseXxxAsync</c> prompt methods with one shape consumed
/// by the single <see cref="IPlayerAgent.ChooseAsync"/> sink.
///
/// <para>
/// <see cref="Candidates"/> is the static candidate pool snapshotted at
/// construction. For candidates that must be enumerated against live state at
/// prompt time, supply a <see cref="CandidateGatherer"/> instead — the agent
/// pipeline invokes it with the live <see cref="GameContext"/> and merges the
/// result into <see cref="Candidates"/> (same contract as
/// <see cref="TargetRequest.CandidateGatherer"/>).
/// </para>
/// </summary>
public sealed record ChoiceRequest(
    ChoiceKind Kind,
    string Description,
    int Min,
    int Max,
    IReadOnlyList<object> Candidates,
    BotIntent Intent = BotIntent.None,
    bool Optional = false,
    Func<GameContext, IReadOnlyList<object>>? CandidateGatherer = null)
{
    /// <summary>
    /// Materialize the live candidate pool — the union of <see cref="Candidates"/>
    /// and any objects produced by <see cref="CandidateGatherer"/> (deduped by
    /// reference). When neither yields anything, returns <see cref="Candidates"/>
    /// as-is. Mirrors <see cref="TargetRequest.ResolveCandidates"/>.
    /// </summary>
    public IReadOnlyList<object> ResolveCandidates(GameContext ctx)
    {
        if (CandidateGatherer == null) return Candidates;
        var gathered = CandidateGatherer(ctx);
        if (gathered == null || gathered.Count == 0) return Candidates;
        if (Candidates.Count == 0) return gathered;
        var merged = new List<object>(Candidates);
        foreach (var g in gathered)
        {
            if (!merged.Any(m => ReferenceEquals(m, g))) merged.Add(g);
        }
        return merged;
    }

    /// <summary>
    /// Return a copy of this request with <see cref="Candidates"/> replaced by
    /// the supplied list. Mirrors <see cref="TargetRequest.WithCandidates"/>.
    /// </summary>
    public ChoiceRequest WithCandidates(IReadOnlyList<object> candidates) =>
        this with { Candidates = candidates };
}
