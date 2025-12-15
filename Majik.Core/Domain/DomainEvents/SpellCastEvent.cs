using Majik.Core.Events;
using Majik.Core.Spells;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when a spell is cast.
/// </summary>
public class SpellCastEvent : GameEvent
{
    /// <summary>
    /// The spell that was cast.
    /// </summary>
    public ISpell Spell { get; }

    public SpellCastEvent(ISpell spell) 
        : base(EventType.Triggered)
    {
        Spell = spell ?? throw new ArgumentNullException(nameof(spell));
    }
}
