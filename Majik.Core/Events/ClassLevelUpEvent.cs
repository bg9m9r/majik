using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Events;

/// <summary>
/// CR 716 — fired when a Class enchantment's level-up activated ability
/// resolves and <see cref="CardData.Classes.ClassState.CurrentLevel"/>
/// advances from <see cref="FromLevel"/> to <see cref="ToLevel"/>. UI / bots
/// subscribe to this to update lobby state and surface higher-level
/// abilities.
/// </summary>
public class ClassLevelUpEvent : GameEvent
{
    /// <summary>The Class permanent that was leveled up.</summary>
    public ICard Source { get; }

    /// <summary>The controller of the Class permanent at resolution time
    /// (CR 716.2 — only the controller may pay the level-up cost).</summary>
    public Player Controller { get; }

    /// <summary>The Class's level before the activation resolved.</summary>
    public int FromLevel { get; }

    /// <summary>The Class's new level after the activation resolved
    /// (always <c>FromLevel + 1</c> — CR 716.4 sequential gate).</summary>
    public int ToLevel { get; }

    public ClassLevelUpEvent(ICard source, Player controller, int fromLevel, int toLevel)
        : base(EventType.ClassLeveledUp)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        FromLevel = fromLevel;
        ToLevel = toLevel;
    }
}
