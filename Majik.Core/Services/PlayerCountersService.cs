using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Services;

/// <summary>
/// CR 122 / CR 614 — player-scoped twin of <see cref="CountersService"/>.
/// Routes counter placement on a <see cref="Player"/> (poison — CR 704.5c;
/// energy — CR 107.16; experience — CR 107.14; or a generic player counter)
/// through the player's attached <see cref="ReplacementBus"/> via a
/// <see cref="PlayerCounterAddIntent"/>, so "players can't get counters"
/// effects (Solemnity, Suncleanser) can rewrite the amount to 0 or cancel it
/// before it commits. After a successful non-zero commit, publishes a
/// <see cref="PlayerCounterAddedEvent"/> so "you get a poison / experience /
/// energy counter" trigger riders can subscribe.
///
/// When no bus is supplied the call falls through to a direct commit — same
/// behaviour as today's untouched player-counter call sites.
/// </summary>
public static class PlayerCountersService
{
    /// <summary>
    /// Place <paramref name="amount"/> counters of <paramref name="type"/>
    /// on <paramref name="target"/>, routing through the supplied
    /// <see cref="ReplacementBus"/> (when non-null) so replacements can
    /// rewrite or cancel the placement before it commits. When
    /// <paramref name="eventBus"/> is supplied AND the commit landed
    /// (amount &gt; 0), a <see cref="PlayerCounterAddedEvent"/> is
    /// published. Returns the actual amount placed (0 when cancelled).
    /// </summary>
    public static int Add(
        Player target,
        CounterType type,
        int amount,
        ReplacementBus? replacements,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(type);
        if (amount <= 0) return 0;

        var intent = new PlayerCounterAddIntent(target, type, amount);

        if (replacements != null)
        {
            var replaced = replacements.Apply(intent);
            if (replaced == null) return 0;          // CR 614.1b — cancelled
            intent = replaced;
        }

        if (intent.Amount <= 0) return 0;            // rewritten to 0 (can't-get lock)
        intent.Target.CommitCounters(intent.Type, intent.Amount);

        // CR 603.6 — publish post-commit so "you get a counter" riders fire
        // exactly once per placement, with the post-replacement amount.
        eventBus?.Publish(new PlayerCounterAddedEvent(intent.Target, intent.Type, intent.Amount));

        return intent.Amount;
    }
}
