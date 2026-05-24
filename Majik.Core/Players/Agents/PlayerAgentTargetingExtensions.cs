using Majik.Core.Cards;
using Majik.Core.Game;

namespace Majik.Core.Players.Agents;

/// <summary>
/// Factory-facing convenience surface for agent-driven target prompts.
///
/// The engine's spell / activation / trigger pipelines call
/// <see cref="IPlayerAgent.ChooseTargetsAsync"/> directly with a fully-built
/// <see cref="TargetRequest"/>. Factories that need to prompt mid-effect
/// (e.g. a resolution-time "pick a card from this list" choice that the
/// declarative TargetRequest path doesn't model) call these helpers
/// instead — they synthesise a one-shot <see cref="TargetRequest"/>,
/// invoke <see cref="IPlayerAgent.ChooseTargetsAsync"/>, and unwrap the
/// result into a typed pick.
///
/// All helpers handle a null agent by falling through to a deterministic
/// "first candidate" pick — the v1 graceful-degrade posture every
/// factory relied on before this surface existed. That keeps existing
/// snapshot tests stable when an agent isn't supplied.
/// </summary>
public static class PlayerAgentTargetingExtensions
{
    /// <summary>
    /// Prompt the agent to choose a single target from <paramref name="candidates"/>.
    /// Returns <see langword="null"/> when the candidate list is empty;
    /// returns the first candidate when <paramref name="agent"/> is null
    /// or the agent yields nothing (deterministic graceful-degrade).
    /// </summary>
    /// <param name="agent">Agent to prompt; null routes to deterministic pick.</param>
    /// <param name="ctx">Live game context for the prompt.</param>
    /// <param name="description">Human-readable target prompt (e.g. "target creature an opponent controls").</param>
    /// <param name="candidates">Pre-filtered legal candidate pool.</param>
    /// <param name="intent">Strategic intent hint for heuristic ranking.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<object?> ChooseTargetAsync(
        this IPlayerAgent? agent,
        GameContext ctx,
        string description,
        IReadOnlyList<object> candidates,
        BotIntent intent = BotIntent.None,
        CancellationToken ct = default)
    {
        if (candidates == null || candidates.Count == 0) return null;
        if (agent == null) return candidates[0];

        var req = new TargetRequest(
            Description: description,
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: candidates,
            Intent: intent);
        var picked = await agent.ChooseTargetsAsync(ctx, req, ct).ConfigureAwait(false);
        if (picked == null || picked.Count == 0) return candidates[0];
        return picked[0];
    }

    /// <summary>
    /// Prompt for up to <paramref name="count"/> targets. Same fallback
    /// rules as <see cref="ChooseTargetAsync"/> — null agent yields the
    /// first <paramref name="count"/> entries of <paramref name="candidates"/>.
    /// </summary>
    public static async Task<IReadOnlyList<object>> ChooseTargetsAsync(
        this IPlayerAgent? agent,
        GameContext ctx,
        string description,
        IReadOnlyList<object> candidates,
        int count,
        BotIntent intent = BotIntent.None,
        CancellationToken ct = default)
    {
        if (count <= 0 || candidates == null || candidates.Count == 0)
            return Array.Empty<object>();
        var take = Math.Min(count, candidates.Count);
        if (agent == null) return candidates.Take(take).ToList();

        var req = new TargetRequest(
            Description: description,
            MinTargets: take,
            MaxTargets: take,
            LegalCandidates: candidates,
            Intent: intent);
        var picked = await agent.ChooseTargetsAsync(ctx, req, ct).ConfigureAwait(false);
        if (picked == null || picked.Count == 0) return candidates.Take(take).ToList();
        return picked.Take(take).ToList();
    }

    /// <summary>
    /// Strongly-typed single-target prompt. Filters <paramref name="candidates"/>
    /// by the cast and forwards through <see cref="ChooseTargetAsync"/>.
    /// </summary>
    public static async Task<T?> ChooseTargetAsync<T>(
        this IPlayerAgent? agent,
        GameContext ctx,
        string description,
        IEnumerable<T> candidates,
        BotIntent intent = BotIntent.None,
        CancellationToken ct = default)
        where T : class
    {
        var pool = candidates.Cast<object>().ToList();
        var picked = await agent.ChooseTargetAsync(ctx, description, pool, intent, ct)
            .ConfigureAwait(false);
        return picked as T;
    }
}
