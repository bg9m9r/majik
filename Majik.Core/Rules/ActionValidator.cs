using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Rules;

/// <summary>
/// Validates player actions before execution.
/// Returns validation results with error messages.
/// </summary>
public class ActionValidator
{
    private readonly RulesEngine _rulesEngine;
    private readonly IEventBus? _eventBus;

    public ActionValidator(RulesEngine? rulesEngine = null, IEventBus? eventBus = null)
    {
        _rulesEngine = rulesEngine ?? new RulesEngine();
        _eventBus = eventBus;
    }

    /// <summary>
    /// Validate a player action.
    /// </summary>
    public ValidationResult ValidateAction(PlayerAction action)
    {
        if (action == null)
        {
            return ValidationResult.Invalid("Action cannot be null");
        }

        // Delegate to specific validation methods based on action type
        return action switch
        {
            CastSpellAction castSpell => ValidateCastSpell(castSpell),
            ActivateAbilityAction activateAbility => ValidateActivateAbility(activateAbility),
            AttackAction attack => ValidateAttack(attack),
            BlockAction block => ValidateBlock(block),
            _ => ValidationResult.Invalid($"Unknown action type: {action.GetType().Name}")
        };
    }

    /// <summary>
    /// Validate casting a spell.
    /// </summary>
    private ValidationResult ValidateCastSpell(CastSpellAction action)
    {
        // Use RulesEngine to validate
        // This is a simplified version - full implementation would check all rules
        return ValidationResult.Valid();
    }

    /// <summary>
    /// Validate activating an ability.
    /// </summary>
    private ValidationResult ValidateActivateAbility(ActivateAbilityAction action)
    {
        // Use RulesEngine to validate
        return ValidationResult.Valid();
    }

    /// <summary>
    /// Validate attacking.
    /// </summary>
    private ValidationResult ValidateAttack(AttackAction action)
    {
        // Use RulesEngine to validate
        return ValidationResult.Valid();
    }

    /// <summary>
    /// Validate blocking.
    /// </summary>
    private ValidationResult ValidateBlock(BlockAction action)
    {
        // Use RulesEngine to validate
        return ValidationResult.Valid();
    }
}

/// <summary>
/// Result of action validation.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; }
    public string? ErrorMessage { get; }
    public RuleViolation? Violation { get; }

    private ValidationResult(bool isValid, string? errorMessage = null, RuleViolation? violation = null)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
        Violation = violation;
    }

    public static ValidationResult Valid()
    {
        return new ValidationResult(true);
    }

    public static ValidationResult Invalid(string errorMessage, RuleViolation? violation = null)
    {
        return new ValidationResult(false, errorMessage, violation);
    }
}

/// <summary>
/// Represents a rule violation.
/// </summary>
public class RuleViolation
{
    public string RuleNumber { get; }
    public string Description { get; }

    public RuleViolation(string ruleNumber, string description)
    {
        RuleNumber = ruleNumber;
        Description = description;
    }
}

/// <summary>
/// Base class for player actions.
/// </summary>
public abstract class PlayerAction
{
}

/// <summary>
/// Action to cast a spell.
/// </summary>
public class CastSpellAction : PlayerAction
{
    public ICard Card { get; }
    public Player Player { get; }

    public CastSpellAction(ICard card, Player player)
    {
        Card = card;
        Player = player;
    }
}

/// <summary>
/// Action to activate an ability.
/// </summary>
public class ActivateAbilityAction : PlayerAction
{
    public IActivatedAbility Ability { get; }
    public Player Player { get; }

    public ActivateAbilityAction(IActivatedAbility ability, Player player)
    {
        Ability = ability;
        Player = player;
    }
}

/// <summary>
/// Action to attack.
/// </summary>
public class AttackAction : PlayerAction
{
    public Creature Creature { get; }
    public Player Player { get; }

    public AttackAction(Creature creature, Player player)
    {
        Creature = creature;
        Player = player;
    }
}

/// <summary>
/// Action to block.
/// </summary>
public class BlockAction : PlayerAction
{
    public Creature Creature { get; }
    public Attacker Attacker { get; }
    public Player Player { get; }

    public BlockAction(Creature creature, Attacker attacker, Player player)
    {
        Creature = creature;
        Attacker = attacker;
        Player = player;
    }
}
