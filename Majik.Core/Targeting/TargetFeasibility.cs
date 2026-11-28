using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.Targeting;

/// <summary>
/// CR 601.2c / 602.2b — can a spell or ability be announced at all, as far as
/// its targets go? A spell or ability that requires a target can be cast or
/// activated only if enough legal targets exist for each of its required
/// target requests. This answers existence only (it stops at the first
/// candidates it needs), using the same pools the cast flow prompts with
/// (<see cref="TargetCollection.ResolveLivePool"/>), so a legal-action list
/// built from it agrees with what the cast would accept. "Up to" requests
/// (minimum 0) need no candidate.
/// </summary>
public static class TargetFeasibility
{
    /// <summary>
    /// CR 601.2c / 700.2 — mirrors <c>SpellCastFlow.CollectTargetsAsync</c>:
    /// a modal spell needs <see cref="SpellDefinition.MinModes"/> modes whose
    /// own targets exist (each chosen mode demands its printed minimum); any
    /// other spell needs every request satisfied.
    /// </summary>
    public static bool HasLegalTargets(SpellDefinition definition, GameContext ctx)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var requests = definition.TargetRequests;
        if (requests.Count == 0) return true;
        var modeCount = definition.Modes.Count;

        // The mode prompt's own per-mode reasons (SpellCastFlow.ModeUnavailableReasons),
        // so "castable" here and "choosable" on the mode prompt never disagree.
        if (modeCount > 0 && (requests.All(r => r.ModeIndex.HasValue) || requests.Count == modeCount))
        {
            var choosable = SpellCastFlow.ModeUnavailableReasons(definition, ctx, synthesize: true)
                .Count(reason => reason == null);
            return choosable >= Math.Max(1, definition.MinModes);
        }

        return HasLegalTargets(requests, ctx);
    }

    /// <summary>CR 601.2c / 602.2b — every request has at least its minimum
    /// number of legal candidates.</summary>
    public static bool HasLegalTargets(IReadOnlyList<TargetRequest> requests, GameContext ctx)
    {
        ArgumentNullException.ThrowIfNull(requests);
        for (var i = 0; i < requests.Count; i++)
        {
            if (!Satisfiable(requests[i], ctx)) return false;
        }
        return true;
    }

    private static bool Satisfiable(TargetRequest request, GameContext ctx)
    {
        if (request.MinTargets <= 0) return true;
        // CR 115.3 — one request's targets are distinct objects, so the pool
        // must hold at least MinTargets of them.
        return TargetCollection.ResolveLivePool(request, ctx, synthesizeWhenEmpty: true).Count >= request.MinTargets;
    }
}