using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.Targeting;

/// <summary>
/// PLAN 01 (Slice E) — the single target-collection pipeline shared by the
/// spell-cast, activated-ability, and triggered-ability paths (CR 601.2c /
/// 602.2b / 603.3). For each <see cref="TargetRequest"/> in declaration order
/// it: resolves any lazy <see cref="TargetRequest.CandidateGatherer"/> against
/// the live <see cref="GameContext"/>, swaps the merged pool in via
/// <see cref="TargetRequest.WithCandidates"/>, prompts the agent through
/// <see cref="IPlayerAgent.ChooseTargetsAsync"/>, and (optionally) enforces the
/// request's minimum cardinality.
///
/// <para>
/// Before this helper, the loop was copy-pasted three times
/// (<c>SpellCastFlow.CollectTargetsAsync</c>,
/// <c>AbilityActivationFlow.ActivateAsync</c>,
/// <c>TriggerManager.PutPendingTriggersOnStackAsync</c>) with subtly different
/// edges (min-cardinality throw only on the spell path; null-agent guard only
/// on the trigger path). Those edges are preserved here via the
/// <paramref name="throwOnInsufficient"/> flag and the nullable
/// <paramref name="agent"/>.
/// </para>
/// </summary>
public static class TargetCollection
{
    /// <summary>
    /// Collect targets for the supplied <paramref name="requests"/>.
    /// </summary>
    /// <param name="requests">Declared target requests, in order.</param>
    /// <param name="card">
    /// The source card, used only for the insufficient-targets error message
    /// (may be null on ability / trigger paths that don't carry one).
    /// </param>
    /// <param name="ctx">Live game context for candidate gathering + prompting.</param>
    /// <param name="agent">
    /// The choosing player's agent. When null (no agent registered for the
    /// controller — trigger path), every request resolves to an empty pick.
    /// </param>
    /// <param name="throwOnInsufficient">
    /// When true (spell-cast path, CR 601.2c) an agent that returns fewer than
    /// <see cref="TargetRequest.MinTargets"/> picks makes the action illegal —
    /// the method throws. When false (ability / trigger paths, which historically
    /// did not gate on min cardinality) the under-filled pick is accepted as-is.
    /// </param>
    public static async Task<List<IReadOnlyList<object>>> CollectAsync(
        IReadOnlyList<TargetRequest> requests,
        ICard? card,
        GameContext ctx,
        IPlayerAgent? agent,
        bool throwOnInsufficient = false,
        CancellationToken ct = default)
    {
        var collected = new List<IReadOnlyList<object>>(requests?.Count ?? 0);
        if (requests == null || requests.Count == 0)
        {
            return collected;
        }

        foreach (var req in requests)
        {
            // Resolve any lazy CandidateGatherer against the live ctx, then
            // hand the agent the merged candidate list. Static LegalCandidates
            // pass through unchanged (ReferenceEquals fast-path).
            var live = req.ResolveCandidates(ctx);
            var promptReq = ReferenceEquals(live, req.LegalCandidates)
                ? req
                : req.WithCandidates(live);

            var picked = agent != null
                ? await agent.ChooseTargetsAsync(ctx, promptReq, ct).ConfigureAwait(false)
                : (IReadOnlyList<object>)Array.Empty<object>();

            if (throwOnInsufficient && picked.Count < req.MinTargets)
            {
                var name = card?.Name ?? "ability";
                throw new InvalidOperationException(
                    $"Cannot resolve {name}: target request '{req.Description}' " +
                    $"needs {req.MinTargets}, agent provided {picked.Count}.");
            }

            collected.Add(picked);
        }

        return collected;
    }
}
