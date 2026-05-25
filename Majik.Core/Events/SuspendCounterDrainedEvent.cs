using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Events;

/// <summary>
/// CR 702.62d — published when the last time counter is removed from a
/// card suspended via <see cref="Majik.Core.Costs.SuspendedCardRegistry"/>.
/// Fires after the counter drain but BEFORE the registry's ready callback
/// dispatches the "cast for free" payoff, so subscribers (UI / logs /
/// bots) can observe the drain event independently of the resulting cast.
///
/// <para>Diagnostic only — does not gate the cast itself. The registry
/// always invokes the ready callback after publishing this event;
/// suppressing the cast (e.g. an effect that says "if you would cast
/// this card via its suspend ability, instead exile it") is a future
/// extension and would gate inside the callback wiring, not by
/// swallowing this event.</para>
/// </summary>
public sealed class SuspendCounterDrainedEvent : GameEvent
{
    /// <summary>The card whose last time counter was just removed.</summary>
    public ICard Card { get; }

    /// <summary>The card's owner — the player who will cast it for free.</summary>
    public Player Owner { get; }

    public SuspendCounterDrainedEvent(ICard card, Player owner)
        : base(EventType.SuspendCounterDrained)
    {
        Card = card ?? throw new ArgumentNullException(nameof(card));
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }
}
