using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;

namespace Majik.Core.Services;

/// <summary>
/// Helper for routing counter placement through the
/// <see cref="ReplacementBus"/> so effects like Hardened Scales and
/// Doubling Season can rewrite the amount before it commits.
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
/// </summary>
public static class CountersService
{
    /// <summary>
    /// Place <paramref name="amount"/> counters of <paramref name="type"/>
    /// on <paramref name="target"/>, routing through the supplied
    /// <see cref="ReplacementBus"/> (when non-null) so replacements can
    /// rewrite or cancel the placement before it commits.
    ///
    /// Returns the actual amount placed (which may differ from
    /// <paramref name="amount"/> after replacements fire); 0 when the
    /// placement was cancelled.
    /// </summary>
    public static int Add(
        Permanent target,
        CounterType type,
        int amount,
        ReplacementBus? replacements)
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
        return intent.Amount;
    }
}
