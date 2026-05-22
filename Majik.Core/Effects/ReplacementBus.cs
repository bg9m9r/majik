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
}
