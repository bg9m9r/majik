using Majik.Core.Cards;

namespace Majik.Core.Events;

/// <summary>
/// Event fired when a card is drawn.
/// </summary>
public class CardDrawnEvent : GameEvent
{
    /// <summary>
    /// The card that was drawn.
    /// </summary>
    public ICard Card { get; }

    /// <summary>
    /// The player who drew the card.
    /// </summary>
    public Players.Player Player { get; }

    public CardDrawnEvent(ICard card, Players.Player player) 
        : base(EventType.CardDrawn)
    {
        Card = card;
        Player = player;
    }
}
