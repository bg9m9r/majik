using Majik.Core.Abilities;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Services;

/// <summary>
/// Service for activating mana abilities.
/// Mana abilities don't use the stack and can be activated during mana payment (Rule 605).
/// </summary>
public class ManaAbilityActivator
{
    private readonly IEventBus? _eventBus;

    public ManaAbilityActivator(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    /// <summary>
    /// Activate a mana ability and add the generated mana to the player's mana pool.
    /// </summary>
    public ManaCost ActivateManaAbility(IManaAbility ability, Player player)
    {
        if (ability == null)
        {
            throw new ArgumentNullException(nameof(ability));
        }

        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (ability.Controller != player)
        {
            throw new InvalidPlayerActionException("Player does not control this mana ability");
        }

        if (!ability.CanActivate())
        {
            throw new InvalidPlayerActionException("Cannot activate mana ability");
        }

        // Activate the ability and generate mana
        var manaGenerated = ability.Activate();

        // Add mana to player's pool. CR 106.4 — when the ability declares a
        // slot-level provenance reaction (Arena of Glory's exert: "if that
        // mana is spent on a creature spell, it gains haste"), tag every
        // colored unit with this ability as source + that reaction, so the
        // payment resolver can fire it precisely when one of those units pays
        // a cost (deferral #1). Vanilla mana abilities add untagged mana.
        var provenance = (ability as Majik.Core.Abilities.ManaAbility)?.ProvenanceReaction;
        if (provenance != null)
        {
            player.AddManaToPool(manaGenerated, provenanceSource: ability, onSpent: provenance);
        }
        else
        {
            player.AddManaToPool(manaGenerated);
        }

        // CR 605 — publish so "whenever a player taps X for mana" triggers
        // (Manabarbs, Badgermole Cub, etc.) and analytics subscribers can
        // observe the activation. Mana abilities don't use the stack
        // (CR 605.3), so this event is the only bus-visible signal.
        _eventBus?.Publish(new ManaAbilityActivatedEvent(ability, player, manaGenerated));

        return manaGenerated;
    }

    /// <summary>
    /// Check if a mana ability can be activated.
    /// </summary>
    public bool CanActivateManaAbility(IManaAbility ability, Player player)
    {
        if (ability == null || player == null)
        {
            return false;
        }

        if (ability.Controller != player)
        {
            return false;
        }

        return ability.CanActivate();
    }
}
