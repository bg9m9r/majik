using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Rules;

/// <summary>
/// Comprehensive rules validation engine.
/// Validates game actions according to Magic: The Gathering rules.
/// </summary>
public class RulesEngine
{
    /// <summary>
    /// Validate if a spell can be cast.
    /// </summary>
    public bool CanCastSpell(ICard card, Player player, bool isMainPhase, bool isStackEmpty)
    {
        if (card == null || player == null)
        {
            return false;
        }

        // Card must be in hand (Rule 601.2a)
        if (card.Zone != ZoneType.Hand)
        {
            return false;
        }

        // Card must be controlled by player
        if (card.Owner != player)
        {
            return false;
        }

        // Check timing restrictions
        if (card is Instant)
        {
            // Instants can be cast anytime (Rule 307.1)
            return true;
        }

        if (card is Sorcery)
        {
            // Sorceries can only be cast during main phase when stack is empty (Rule 307.1)
            return isMainPhase && isStackEmpty;
        }

        // Other card types have different rules
        return false;
    }

    /// <summary>
    /// Validate if an ability can be activated.
    /// </summary>
    public bool CanActivateAbility(IActivatedAbility ability, Player player)
    {
        if (ability == null || player == null)
        {
            return false;
        }

        // Ability must be controlled by player
        if (ability.Controller != player)
        {
            return false;
        }

        // Source must be on battlefield (for most abilities)
        if (ability.Source is ICard sourceCard && sourceCard.Zone != ZoneType.Battlefield)
        {
            return false;
        }

        // Mana abilities can be activated anytime (Rule 605.3a)
        if (ability is IManaAbility)
        {
            return true;
        }

        // Other abilities follow normal timing rules
        return true;
    }

    /// <summary>
    /// Validate if a creature can attack.
    /// Delegates to CombatValidator for detailed validation.
    /// </summary>
    public bool CanAttack(Creature creature, Player activePlayer)
    {
        if (creature == null || activePlayer == null)
        {
            return false;
        }

        // Use CombatValidator for detailed checks
        var validator = new Combat.CombatValidator();
        return validator.CanAttack(creature, activePlayer);
    }

    /// <summary>
    /// Validate if a creature can block.
    /// Delegates to CombatValidator for detailed validation.
    /// </summary>
    public bool CanBlock(Creature creature, Attacker attacker, Player defendingPlayer)
    {
        if (creature == null || attacker == null || defendingPlayer == null)
        {
            return false;
        }

        // Use CombatValidator for detailed checks
        var validator = new Combat.CombatValidator();
        return validator.CanBlock(creature, attacker, defendingPlayer);
    }

    /// <summary>
    /// Validate zone transition.
    /// </summary>
    public bool CanMoveCard(ICard card, ZoneType fromZone, ZoneType toZone)
    {
        if (card == null)
        {
            return false;
        }

        // Card must be in source zone
        if (card.Zone != fromZone)
        {
            return false;
        }

        // Basic validation - more specific rules can be added
        return true;
    }

    /// <summary>
    /// Validate mana payment.
    /// </summary>
    public bool CanPayMana(Player player, ManaCost cost)
    {
        if (player == null || cost == null)
        {
            return false;
        }

        return player.ManaPool.CanPay(cost);
    }

    /// <summary>
    /// Validate if a spell can be cast in current phase.
    /// </summary>
    public bool CanCastInPhase(ICard card, PhaseStateType? currentPhase, bool isStackEmpty)
    {
        if (card == null)
        {
            return false;
        }

        if (card is Instant)
        {
            // Instants can be cast anytime
            return true;
        }

        if (card is Sorcery)
        {
            // Sorceries can only be cast during main phase when stack is empty
            return currentPhase is { } p && p.IsMain() && isStackEmpty;
        }

        return false;
    }

    /// <summary>
    /// Validate if player can take actions in current game state.
    /// </summary>
    public bool CanTakeAction(Player player, bool isStackEmpty, bool allPlayersPassed)
    {
        if (player == null)
        {
            return false;
        }

        // Player must not have lost
        if (player.HasLost)
        {
            return false;
        }

        // Basic validation - more rules can be added
        return true;
    }
}
