using Majik.Core.StateMachine;

namespace Majik.Core.Events;

/// <summary>
/// Event fired when an extra phase is added to the queue.
/// </summary>
public class ExtraPhaseAddedEvent : GameEvent
{
    /// <summary>
    /// The type of phase that was added.
    /// </summary>
    public StepStateType PhaseType { get; }

    public ExtraPhaseAddedEvent(StepStateType phaseType) 
        : base(EventType.PhaseStarted)
    {
        PhaseType = phaseType;
    }
}
