using Majik.Core.Players;

namespace Majik.Core.Events;

/// <summary>
/// Event fired when a player loses the game.
/// </summary>
public class PlayerLostEvent : GameEvent
{
    /// <summary>
    /// The player who lost.
    /// </summary>
    public Player Player { get; }

    public PlayerLostEvent(Player player) 
        : base(EventType.GameEnded)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
    }
}
