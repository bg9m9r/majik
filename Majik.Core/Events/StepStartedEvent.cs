using Majik.Core.Players;
using Majik.Core.StateMachine;

namespace Majik.Core.Events;

/// <summary>
/// Event fired when a step starts.
/// </summary>
public class StepStartedEvent : GameEvent
{
    /// <summary>
    /// The type of step that started.
    /// </summary>
    public StepStateType StepType { get; }

    /// <summary>
    /// The active player.
    /// </summary>
    public Player Player { get; }

    public StepStartedEvent(StepStateType stepType, Player player) 
        : base(EventType.StepStarted)
    {
        StepType = stepType;
        Player = player ?? throw new ArgumentNullException(nameof(player));
    }
}
