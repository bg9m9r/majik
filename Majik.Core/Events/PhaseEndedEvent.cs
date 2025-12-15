using Majik.Core.Players;
using Majik.Core.StateMachine;

namespace Majik.Core.Events;

/// <summary>
/// Event fired when a phase ends.
/// </summary>
public class PhaseEndedEvent : GameEvent
{
    /// <summary>
    /// The type of phase that ended.
    /// </summary>
    public PhaseStateType PhaseType { get; }

    /// <summary>
    /// The active player.
    /// </summary>
    public Player Player { get; }

    public PhaseEndedEvent(PhaseStateType phaseType, Player player) 
        : base(EventType.PhaseEnded)
    {
        PhaseType = phaseType;
        Player = player ?? throw new ArgumentNullException(nameof(player));
    }
}
