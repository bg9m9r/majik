using Majik.Core.Players;
using Majik.Core.StateMachine;

namespace Majik.Core.Events;

/// <summary>
/// Event fired when a phase starts.
/// </summary>
public class PhaseStartedEvent : GameEvent
{
    /// <summary>
    /// The type of phase that started.
    /// </summary>
    public PhaseStateType PhaseType { get; }

    /// <summary>
    /// The active player.
    /// </summary>
    public Player Player { get; }

    public PhaseStartedEvent(PhaseStateType phaseType, Player player) 
        : base(EventType.PhaseStarted)
    {
        PhaseType = phaseType;
        Player = player ?? throw new ArgumentNullException(nameof(player));
    }
}
