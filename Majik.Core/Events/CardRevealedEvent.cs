using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Events;

/// <summary>
/// Event fired when a card is revealed from a hidden zone (typically a player's
/// hand) per CR 701.16. A card becomes public until the effect that caused the
/// reveal stops applying.
///
/// The engine emits this for every card that becomes visible — e.g. for a
/// "reveal your hand" effect, one <c>CardRevealedEvent</c> is fired per card
/// in that hand. Clients (portal) can use this to flash the opponent's hand
/// card(s) briefly without re-snapshotting hidden-info state.
///
/// Distinct from <see cref="CardMovedEvent"/>: a reveal does NOT change the
/// card's zone. The card stays in hand (or wherever); only its visibility
/// to other players changes for the duration of the revealing effect.
/// </summary>
public class CardRevealedEvent : GameEvent
{
    /// <summary>The card that became public.</summary>
    public ICard Card { get; }

    /// <summary>The player who owns the revealed card.</summary>
    public Player Player { get; }

    /// <summary>The zone the card was revealed from (typically <see cref="ZoneType.Hand"/>).</summary>
    public ZoneType From { get; }

    /// <summary>
    /// Short tag describing why the reveal happened — e.g. "Thoughtseize",
    /// "Duress", "Castigate", "RevealHandMayChoose". Useful for UI affordance
    /// and debugging; not load-bearing for game state.
    /// </summary>
    public string Reason { get; }

    public CardRevealedEvent(ICard card, Player player, ZoneType from, string reason)
        : base(EventType.CardRevealed)
    {
        Card = card;
        Player = player;
        From = from;
        Reason = reason;
    }
}
