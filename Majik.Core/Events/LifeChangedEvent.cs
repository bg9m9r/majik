using Majik.Core.Players;

namespace Majik.Core.Events;

/// <summary>
/// Event fired when a player's life total changes.
/// </summary>
public class LifeChangedEvent : GameEvent
{
    /// <summary>
    /// The player whose life changed.
    /// </summary>
    public Player Player { get; }

    /// <summary>
    /// The previous life total.
    /// </summary>
    public int PreviousLife { get; }

    /// <summary>
    /// The new life total.
    /// </summary>
    public int NewLife { get; }

    public LifeChangedEvent(Player player, int previousLife, int newLife) 
        : base(EventType.LifeChanged)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        PreviousLife = previousLife;
        NewLife = newLife;
    }
}
