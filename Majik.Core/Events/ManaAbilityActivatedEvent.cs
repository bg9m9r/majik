using Majik.Core.Abilities;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Events;

/// <summary>
/// CR 605 — fired whenever a mana ability has been activated and the
/// generated mana has been added to the activator's mana pool. Mana
/// abilities don't use the stack (CR 605.3), so this event is the only
/// observable signal subscribers get; downstream "whenever a player taps
/// a land for mana" triggers (Manabarbs) and analytics/log subscribers
/// consume this event.
///
/// <see cref="Source"/> is the raw <see cref="IManaAbility.Source"/> from
/// the ability (typically the <see cref="Cards.Permanent"/> that was
/// tapped). Subscribers that need to filter on "the source is a land"
/// pattern-match on that property.
/// </summary>
public class ManaAbilityActivatedEvent : GameEvent
{
    /// <summary>The mana ability that was activated.</summary>
    public IManaAbility Ability { get; }

    /// <summary>The player who activated the ability.</summary>
    public Player Player { get; }

    /// <summary>The mana that was added to the player's pool.</summary>
    public ManaCost ManaGenerated { get; }

    /// <summary>The ability's source object (typically a Permanent — the
    /// permanent that was tapped).</summary>
    public object Source { get; }

    public ManaAbilityActivatedEvent(
        IManaAbility ability, Player player, ManaCost manaGenerated)
        : base(EventType.ManaAdded)
    {
        Ability = ability ?? throw new ArgumentNullException(nameof(ability));
        Player = player ?? throw new ArgumentNullException(nameof(player));
        ManaGenerated = manaGenerated ?? throw new ArgumentNullException(nameof(manaGenerated));
        Source = ability.Source;
    }
}
