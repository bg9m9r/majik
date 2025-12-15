namespace Majik.Core.Events;

/// <summary>
/// Event fired when a phase or state changes.
/// </summary>
public class PhaseChangedEvent : GameEvent
{
    /// <summary>
    /// The previous phase/state name.
    /// </summary>
    public string? PreviousPhase { get; }

    /// <summary>
    /// The new phase/state name.
    /// </summary>
    public string CurrentPhase { get; }

    public PhaseChangedEvent(string? previousPhase, string currentPhase) 
        : base(EventType.PhaseStarted)
    {
        PreviousPhase = previousPhase;
        CurrentPhase = currentPhase;
    }
}
