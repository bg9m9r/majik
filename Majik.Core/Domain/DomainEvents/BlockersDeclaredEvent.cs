using Majik.Core.Events;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when blockers are declared (Rule 509).
/// </summary>
public class BlockersDeclaredEvent : GameEvent
{
    public Majik.Core.Combat.Combat Combat { get; }

    public BlockersDeclaredEvent(Majik.Core.Combat.Combat combat)
        : base(EventType.BlockersDeclared)
    {
        Combat = combat ?? throw new ArgumentNullException(nameof(combat));
    }
}
