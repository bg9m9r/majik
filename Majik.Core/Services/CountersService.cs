using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;

namespace Majik.Core.Services;

/// <summary>
/// Helper for routing counter placement through the
/// <see cref="ReplacementBus"/> so effects like Hardened Scales and
/// Doubling Season can rewrite the amount before it commits, AND for
/// publishing a post-commit <see cref="CounterAddedEvent"/> so
/// "Whenever one or more counters are put on …" triggers (Animation
/// Module, Conclave Mentor) can subscribe.
///
/// Callers that want their counter placement to honour replacement
/// effects should call <see cref="Add"/> instead of mutating
/// <see cref="Permanent.Counters"/> directly. When no bus is supplied
/// the call falls through to a direct add — same behaviour as today's
/// untouched call sites.
///
/// CR 614 — replacement effects observe a single "would happen" event
/// for the entire placement, regardless of count. CR 121.2 — modifiers
/// that change the count (Hardened Scales' "+1", Doubling Season's
/// "twice that many") apply during the replacement, not afterwards.
/// CR 603.6 — the post-commit trigger event carries the post-replacement
/// amount so "Whenever one or more counters are put on …" subscribers
/// see the same count Hardened Scales / Doubling Season committed.
/// </summary>
public static class CountersService
{
    /// <summary>
    /// Place <paramref name="amount"/> counters of <paramref name="type"/>
    /// on <paramref name="target"/>, routing through the supplied
    /// <see cref="ReplacementBus"/> (when non-null) so replacements can
    /// rewrite or cancel the placement before it commits. When
    /// <paramref name="eventBus"/> is supplied AND the commit landed
    /// (amount &gt; 0), a <see cref="CounterAddedEvent"/> is published
    /// so subscribers ("Whenever one or more counters are put on …"
    /// triggers) can fire.
    ///
    /// Returns the actual amount placed (which may differ from
    /// <paramref name="amount"/> after replacements fire); 0 when the
    /// placement was cancelled.
    /// </summary>
    public static int Add(
        Permanent target,
        CounterType type,
        int amount,
        ReplacementBus? replacements,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (amount <= 0) return 0;

        var intent = new CounterAddIntent(target, type, amount);

        if (replacements != null)
        {
            var replaced = replacements.Apply(intent);
            if (replaced == null) return 0;
            intent = replaced;
        }

        if (intent.Amount <= 0) return 0;
        target.Counters.Add(intent.Type, intent.Amount);

        // CR 603.6 — publish the post-commit event so "Whenever one or
        // more counters are put on …" triggers can fire. Single event
        // per call regardless of count (CR 603.6b — a single counter-
        // placement instance, not per-counter), so Animation Module's
        // "may pay {1}" rider fires exactly once per CountersService.Add.
        eventBus?.Publish(new CounterAddedEvent(intent.Target, intent.Type, intent.Amount));

        return intent.Amount;
    }
}
