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
    /// <summary>
    /// The attacking permanent. Typed <see cref="Permanent"/> (not
    /// <see cref="Creature"/>) so an animated NON-creature combatant — a manland
    /// (CR 613.1c) — names ITSELF here when it attacks, letting Restless-land
    /// "whenever ~ attacks" triggers finally observe their own land (deferral
    /// <c>animated-noncreature-as-combatant</c>, 4B). A real <see cref="Creature"/>
    /// is a <see cref="Permanent"/>, so existing trigger binders that read
    /// <c>.Controller</c> / <c>ReferenceEquals</c> are unaffected.
    /// </summary>
    public Permanent Attacker { get; }
    public object DefendingPlayerOrPlaneswalker { get; }

    public CreatureAttacksEvent(Permanent attacker, object defendingPlayerOrPlaneswalker)
        : base(EventType.PhaseEnded)
    {
        Attacker = attacker ?? throw new ArgumentNullException(nameof(attacker));
        DefendingPlayerOrPlaneswalker = defendingPlayerOrPlaneswalker
            ?? throw new ArgumentNullException(nameof(defendingPlayerOrPlaneswalker));
    }
}
