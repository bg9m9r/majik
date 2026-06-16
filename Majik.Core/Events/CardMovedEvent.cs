using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Events;

/// <summary>
/// Event fired when a card moves between zones.
/// </summary>
public class CardMovedEvent : GameEvent
{
    /// <summary>
    /// The card that was moved.
    /// </summary>
    public ICard Card { get; }

    /// <summary>
    /// The zone the card moved from.
    /// </summary>
    public ZoneType FromZone { get; }

    /// <summary>
    /// The zone the card moved to.
    /// </summary>
    public ZoneType ToZone { get; }

    /// <summary>
    /// Last-known-information controller of the moved object — the player who
    /// controlled the card at the instant of the move, captured BEFORE any
    /// controller reset the zone change performs.
    ///
    /// CR 603.10 — leaves-the-battlefield / dies abilities, and any trigger
    /// that branches on "a creature you control" vs "a creature an opponent
    /// controls", must read the dying object's controller from last-known
    /// information at the moment it left the battlefield, NOT off the live
    /// card. Once a permanent moves Battlefield → Graveyard the engine resets
    /// <see cref="ICard.Controller"/> back to the owner (CR 110.2 — a card not
    /// on the battlefield/stack is controlled by its owner), so the live
    /// <see cref="ICard.Controller"/> is stale for these triggers. This
    /// snapshot preserves the correct controller.
    ///
    /// For battlefield exits via the production <c>ZoneService</c> path this is
    /// the controller as of immediately before the move. For direct
    /// construction (shape / dispatcher tests) it defaults to the card's
    /// current <see cref="ICard.Controller"/>, which is the correct LKI when no
    /// reset has happened yet.
    /// </summary>
    public Player? LkiController { get; }

    public CardMovedEvent(ICard card, ZoneType fromZone, ZoneType toZone)
        : this(card, fromZone, toZone, card.Controller)
    {
    }

    public CardMovedEvent(ICard card, ZoneType fromZone, ZoneType toZone, Player? lkiController)
        : base(EventType.CardMoved)
    {
        Card = card;
        FromZone = fromZone;
        ToZone = toZone;
        LkiController = lkiController;
    }
}
