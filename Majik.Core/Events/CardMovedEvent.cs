using Majik.Core.Cards;
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

    public CardMovedEvent(ICard card, ZoneType fromZone, ZoneType toZone) 
        : base(EventType.CardMoved)
    {
        Card = card;
        FromZone = fromZone;
        ToZone = toZone;
    }
}
