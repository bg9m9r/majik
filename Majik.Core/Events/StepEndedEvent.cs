using Majik.Core.Players;
using Majik.Core.StateMachine;

namespace Majik.Core.Events;

/// <summary>
/// Event fired when a step ends.
/// </summary>
public class StepEndedEvent : GameEvent
{
    /// <summary>
    /// The type of step that ended.
    /// </summary>
    public PhaseStateType StepType { get; }

    /// <summary>
    /// The active player.
    /// </summary>
    public Player Player { get; }

    public StepEndedEvent(PhaseStateType stepType, Player player) 
        : base(EventType.StepEnded)
    {
        StepType = stepType;
        Player = player ?? throw new ArgumentNullException(nameof(player));
    }
}
