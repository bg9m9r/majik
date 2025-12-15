using Majik.Core.Events;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when all players have passed priority in succession.
/// If stack is empty, phase can end. If stack is not empty, top object resolves.
/// </summary>
public class AllPlayersPassedEvent : GameEvent
{
    /// <summary>
    /// Whether the stack is empty (phase can end if true).
    /// </summary>
    public bool StackIsEmpty { get; }

    public AllPlayersPassedEvent(bool stackIsEmpty) 
        : base(EventType.PhaseEnded)
    {
        StackIsEmpty = stackIsEmpty;
    }
}
