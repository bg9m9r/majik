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

        return Produce(ability, player, _eventBus);
    }

    /// <summary>
    /// CR 605 — the one place a mana ability produces mana, for a player's own
    /// activation (above) and for a payment that taps the source
    /// (ManaPaymentResolver): activate it (tap / pay its cost), add exactly
    /// the mana it produced to <paramref name="player"/>'s pool, then publish
    /// <see cref="ManaAbilityActivatedEvent"/> carrying that mana.
    /// <para>CR 106.4 — the mana is stamped with the ability's spend
    /// restriction (Ancient Ziggurat: "spend this mana only to cast a creature
    /// spell") and provenance reaction (Arena of Glory's exert haste) so the
    /// payment gate and the reaction see it however the mana was produced.</para>
    /// <para>The event is the only bus-visible signal of a mana ability (it
    /// doesn't use the stack, CR 605.3): "whenever a player taps X for mana"
    /// triggers (Manabarbs, Overgrowth, Mirari's Wake) key on it.</para>
    /// </summary>
    internal static ManaCost Produce(IManaAbility ability, Player player, IEventBus? eventBus)
    {
        var manaGenerated = ability.Activate();

        var concrete = ability as Majik.Core.Abilities.ManaAbility;
        var restriction = concrete?.SpendRestriction;
        var reaction = concrete?.ProvenanceReaction;
        if (restriction != null || reaction != null)
        {
            player.AddManaToPool(manaGenerated, provenanceSource: ability, onSpent: reaction, restriction: restriction);
        }
        else
        {
            player.AddManaToPool(manaGenerated);
        }

        eventBus?.Publish(new ManaAbilityActivatedEvent(ability, player, manaGenerated));
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
