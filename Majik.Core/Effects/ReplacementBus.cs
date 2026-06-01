using Majik.Core.Abilities;

namespace Majik.Core.Effects;

/// <summary>
/// Central registry of replacement effects. Callers about to apply an
/// effect to game state instead build an intent object and pass it
/// through <see cref="Apply{TIntent}"/>; the bus mutates or cancels it
/// before the caller commits.
///
/// CR 616 — when multiple replacements apply, the player affected chooses
/// the order. This MVP defers that choice: applies in registration order,
/// tracking which have fired (via `tag`) so each fires at most once per
/// intent. Phase 17.6 can add agent-driven ordering.
/// </summary>
public sealed class ReplacementBus
{
    private readonly List<object> _effects = new();

    public void Register<TIntent>(IReplacementEffect<TIntent> effect) where TIntent : class
    {
        if (effect == null) throw new ArgumentNullException(nameof(effect));
        _effects.Add(effect);
    }

    public void Unregister<TIntent>(IReplacementEffect<TIntent> effect) where TIntent : class
    {
        if (effect == null) return;
        _effects.Remove(effect);
    }

    /// <summary>
    /// Find an already-registered <see cref="IReplacementEffect{TIntent}"/>
    /// whose <see cref="IReplacementEffect{TIntent}.Tag"/> matches
    /// <paramref name="tag"/> by reference equality, or null when no such
    /// effect is registered. Used by global-replacement factories
    /// (<see cref="FinalityCounterReplacement"/>) to make
    /// <c>Register</c> idempotent across factory calls without forcing
    /// callers to coordinate.
    /// </summary>
    public IReplacementEffect<TIntent>? FindByTag<TIntent>(object tag) where TIntent : class
    {
        if (tag == null) return null;
        foreach (var raw in _effects)
        {
            if (raw is not IReplacementEffect<TIntent> eff) continue;
            if (ReferenceEquals(eff.Tag, tag)) return eff;
        }
        return null;
    }

    /// <summary>
    /// Drop all replacement effects flagged as expiring at end of turn —
    /// per-turn shields like Fog ("prevent all combat damage this turn")
    /// and one-shot "prevent the next N damage this turn" effects.
    /// Called from <c>TurnDriver</c> during the cleanup step so the
    /// shield list parallels <see cref="ContinuousEffectsService.ExpireEndOfTurn"/>.
    /// </summary>
    public void ExpireEndOfTurn()
    {
        _effects.RemoveAll(e => e is IEndOfTurnExpirable eot && eot.ExpiresAtEndOfTurn);
    }

    /// <summary>
    /// Push an intent through the bus. Returns transformed intent, or null
    /// if any replacement cancelled it. Each registered effect fires at
    /// most once per Apply call (CR 616.1c — self-replacement).
    /// </summary>
    public TIntent? Apply<TIntent>(TIntent intent) where TIntent : class
    {
        if (intent == null) return null;

        var history = new List<object>();
        var current = intent;
        var oneShotFired = new List<object>();

        while (true)
        {
            var fired = false;
            foreach (var raw in _effects.ToList())
            {
                if (raw is not IReplacementEffect<TIntent> eff) continue;
                // Each effect fires at most once per intent (CR 616.1c).
                if (history.Contains((object?)eff.Tag ?? eff)) continue;
                if (!eff.Applies(current, history)) continue;

                var next = eff.Replace(current, history);
                history.Add(eff.Tag ?? eff);
                if (eff.OneShot) oneShotFired.Add(eff);

                if (next == null)
                {
                    foreach (var o in oneShotFired) _effects.Remove(o);
                    return null;
                }

                current = next;
                fired = true;
                break;
            }

            if (!fired) break;
        }

        foreach (var o in oneShotFired) _effects.Remove(o);
        return current;
    }

    /// <summary>
    /// PLAN 08 — async twin of <see cref="Apply{TIntent}"/>. Pushes an intent
    /// through the bus, <c>await</c>ing each applicable effect's
    /// <see cref="IReplacementEffect{TIntent}.ReplaceAsync"/> off the live
    /// <paramref name="ctx"/> so prompting replacements (shock land, Mox
    /// Diamond, Dredge) genuinely await the agent instead of blocking a
    /// thread-pool thread on a sync-over-async bridge. Non-prompting
    /// replacements inherit the default <c>ReplaceAsync</c> shim over their
    /// synchronous <see cref="IReplacementEffect{TIntent}.Replace"/>, so this
    /// path produces identical results to <see cref="Apply{TIntent}"/> for
    /// every effect that does not override <c>ReplaceAsync</c>.
    /// Each registered effect fires at most once per call (CR 616.1c).
    /// </summary>
    public async ValueTask<TIntent?> ApplyAsync<TIntent>(TIntent intent, ResolutionContext ctx)
        where TIntent : class
    {
        if (intent == null) return null;
        ArgumentNullException.ThrowIfNull(ctx);

        var history = new List<object>();
        var current = intent;
        var oneShotFired = new List<object>();

        while (true)
        {
            var fired = false;
            foreach (var raw in _effects.ToList())
            {
                if (raw is not IReplacementEffect<TIntent> eff) continue;
                // Each effect fires at most once per intent (CR 616.1c).
                if (history.Contains((object?)eff.Tag ?? eff)) continue;
                if (!eff.Applies(current, history)) continue;

                var next = await eff.ReplaceAsync(current, history, ctx).ConfigureAwait(false);
                history.Add(eff.Tag ?? eff);
                if (eff.OneShot) oneShotFired.Add(eff);

                if (next == null)
                {
                    foreach (var o in oneShotFired) _effects.Remove(o);
                    return null;
                }

                current = next;
                fired = true;
                break;
            }

            if (!fired) break;
        }

        foreach (var o in oneShotFired) _effects.Remove(o);
        return current;
    }
}
