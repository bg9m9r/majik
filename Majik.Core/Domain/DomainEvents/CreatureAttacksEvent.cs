using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// CR 508.1f — fires when a creature is declared as an attacker. One event
/// per attacking creature so binders for "Whenever ~ attacks, …" triggers
/// can hook a per-attacker condition without needing to walk the whole
/// CombatPlan.
/// </summary>
public class CreatureAttacksEvent : GameEvent
{
    public Creature Attacker { get; }
    public object DefendingPlayerOrPlaneswalker { get; }

    public CreatureAttacksEvent(Creature attacker, object defendingPlayerOrPlaneswalker)
        : base(EventType.PhaseEnded)
    {
        Attacker = attacker ?? throw new ArgumentNullException(nameof(attacker));
        DefendingPlayerOrPlaneswalker = defendingPlayerOrPlaneswalker
            ?? throw new ArgumentNullException(nameof(defendingPlayerOrPlaneswalker));
    }
}
