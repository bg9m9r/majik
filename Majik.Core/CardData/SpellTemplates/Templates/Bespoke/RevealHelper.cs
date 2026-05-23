using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Helper for reveal-hand spell templates. Emits one
/// <see cref="CardRevealedEvent"/> per card in the target player's hand so
/// the wire delta (and the portal client subscribed via SignalR) can flash
/// the opponent's hand contents per CR 701.16.
///
/// Centralised so every "Target opponent reveals their hand" template
/// (Thoughtseize, Duress, Castigate, Inquisition of Kozilek, …) emits the
/// same shape — and so future Reveal* templates can opt in with one line.
/// </summary>
internal static class RevealHelper
{
    /// <summary>
    /// Publish a <see cref="CardRevealedEvent"/> for every card currently in
    /// <paramref name="player"/>'s hand. No-op when <paramref name="eventBus"/>
    /// is null (templates may be bound without a bus in unit fixtures).
    ///
    /// The hand snapshot is materialised before publish so subscribers can
    /// mutate the hand (e.g. trigger a discard) without invalidating the
    /// reveal sequence.
    /// </summary>
    public static void RevealHand(IEventBus? eventBus, Player player, string reason)
    {
        if (eventBus is null) return;
        var hand = player.Zones.Hand.GetCards().ToList();
        foreach (var card in hand)
        {
            eventBus.Publish(new CardRevealedEvent(card, player, ZoneType.Hand, reason));
        }
    }
}
