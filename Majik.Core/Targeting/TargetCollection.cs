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
        CancellationToken ct = default,
        Action<IReadOnlyList<IReadOnlyList<object>>>? onSlotChosen = null)
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
            // pass through unchanged (ReferenceEquals fast-path). When the card
            // ships NO machine-readable pool the central TargetCandidateService
            // fills it from the description's category (incl. players) so the
            // portal can render legal targets — see ResolveLivePool.
            var live = ResolveLivePool(
                req, ctx, agent?.WantsSynthesizedTargetCandidates ?? false);

            // CR 601.2c — "any other target": the earlier request's picks are
            // not candidates. With no pool of its own, the pool is the earlier
            // request's ("any other target" is any target), synthesized even
            // for a bot (which otherwise picks from an empty pool) so there is
            // a pool to filter.
            IReadOnlyList<object> earlier = Array.Empty<object>();
            if (req.DistinctFromRequest is { } other && other >= 0 && other < collected.Count)
            {
                earlier = collected[other];
                if (live.Count == 0) live = ResolveLivePool(requests[other], ctx, synthesizeWhenEmpty: true);
                live = live.Where(c => !earlier.Contains(c)).ToList();
            }

            // CR 601.2c — an agent that can only pick from the offered pool
            // can't answer a required request whose pool is too small: the
            // cast is illegal, so reverse it now rather than raise a prompt
            // with no legal answer.
            if (throwOnInsufficient && agent is { WantsSynthesizedTargetCandidates: true }
                && live.Count < req.MinTargets)
            {
                throw new InvalidOperationException(
                    $"Cannot cast {card?.Name ?? "ability"}: target request '{req.Description}' " +
                    $"needs {req.MinTargets} legal target(s); there are {live.Count} (CR 601.2c).");
            }

            var promptReq = ReferenceEquals(live, req.LegalCandidates)
                ? req
                : req.WithCandidates(live);

            // Only skip the prompt for agents that rely on a synthesized pool
            // (WantsSynthesizedTargetCandidates = true, i.e. RemoteAgent) when
            // that pool came back empty — firing a SignalR prompt with 0
            // candidates deadlocks the portal (issue #3495). Bots opt OUT of
            // synthesis (WantsSynthesizedTargetCandidates = false) and handle
            // empty pools via their own TargetPolicy heuristic; the guard must
            // NOT skip them or targeted-spell tests break.
            var picked = agent != null && (live.Count > 0 || !agent.WantsSynthesizedTargetCandidates)
                ? await agent.ChooseTargetsAsync(ctx, promptReq, ct).ConfigureAwait(false)
                : (IReadOnlyList<object>)Array.Empty<object>();

            if (earlier.Count > 0 && picked.Any(earlier.Contains))
            {
                if (throwOnInsufficient)
                {
                    throw new InvalidOperationException(
                        $"Cannot resolve {card?.Name ?? "ability"}: target request " +
                        $"'{req.Description}' must be a different target (CR 601.2c).");
                }
                picked = picked.Where(p => !earlier.Contains(p)).ToList();
            }

            if (throwOnInsufficient && picked.Count < req.MinTargets)
            {
                var name = card?.Name ?? "ability";
                throw new InvalidOperationException(
                    $"Cannot resolve {name}: target request '{req.Description}' " +
                    $"needs {req.MinTargets}, agent provided {picked.Count}.");
            }

            collected.Add(picked);
            // Progress for display (the spell-being-cast snapshot): the slots
            // chosen so far. Not read by rules code.
            onSlotChosen?.Invoke(collected.ToArray());
        }

        return collected;
    }

    /// <summary>
    /// CR 115 — resolve the live candidate pool for a single request. First
    /// resolves the request's own <see cref="Players.Agents.TargetRequest.CandidateGatherer"/>
    /// (bespoke "you control" / color / power filters always win). ONLY when
    /// that yields an EMPTY pool does it fall back to the central
    /// <see cref="TargetCandidateService.GatherCandidates"/> for the
    /// description's category — giving "any target"-style requests that ship no
    /// gatherer a complete legal pool (creatures, players, planeswalkers,
    /// permanents, stack spells, graveyard cards). No behaviour change when a
    /// card already supplies candidates. The central fallback is applied only
    /// when <paramref name="synthesizeWhenEmpty"/> is true (the consuming agent
    /// opted in via IPlayerAgent.WantsSynthesizedTargetCandidates) — bots keep
    /// the empty-pool path so their TargetPolicy heuristic is unchanged.
    /// </summary>
    internal static IReadOnlyList<object> ResolveLivePool(
        TargetRequest req, GameContext ctx, bool synthesizeWhenEmpty = true)
    {
        var live = req.ResolveCandidates(ctx);
        if (synthesizeWhenEmpty && live.Count == 0)
        {
            var central = TargetCandidateService.GatherCandidates(
                req.Description, ctx, ctx.Self);
            if (central.Count > 0) return central;
        }
        return live;
    }
}
