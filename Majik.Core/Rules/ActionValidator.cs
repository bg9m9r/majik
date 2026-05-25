using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

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
        // CR 117.1 / 302.1 — non-instant non-Flash cards need sorcery speed
        // (active player's main phase, empty stack). Caller marks the
        // timing window via SorcerySpeedAvailable.
        if (!action.SorcerySpeedAvailable
            && !TimingRules.CanCastAtInstantSpeed(action.Card))
        {
            return ValidationResult.Invalid(
                $"{action.Card.Name} requires sorcery speed",
                new RuleViolation("117.1", "non-instant cast at non-sorcery speed"));
        }

        // CR 601.3 / 117.1a — external sorcery-speed restrictions (e.g.
        // Teferi, Time Raveler: "Each opponent can cast spells only any
        // time they could cast a sorcery."). Even an instant or
        // Flash-bearing card is forced to sorcery speed when the casting
        // player is restricted.
        if (!action.SorcerySpeedAvailable
            && action.Player != null
            && CastingRestrictions.MustCastAtSorcerySpeed(action.Player))
        {
            return ValidationResult.Invalid(
                $"{action.Player.Name} can cast spells only at sorcery speed",
                new RuleViolation("117.1a", "external sorcery-speed restriction"));
        }

        // CR 113.6 / 601.3 — external cast-from-hand-only restrictions
        // (e.g. Drannith Magistrate: "Your opponents can't cast spells
        // from anywhere other than their hands."). When the casting
        // player is restricted, reject any cast whose declared source
        // zone is not the hand — including spells cast from exile
        // (cascade, suspend, foretell, alt-cost from-exile flows), the
        // graveyard (flashback / disturb / aftermath / escape /
        // jump-start), the library (Mishra's Workshop-style tutors that
        // also cast), or the command zone.
        if (action.Player != null
            && action.FromZone.HasValue
            && action.FromZone.Value != ZoneType.Hand
            && CastingRestrictions.MustCastFromHand(action.Player))
        {
            return ValidationResult.Invalid(
                $"{action.Player.Name} can't cast spells from {action.FromZone.Value}",
                new RuleViolation("113.6", "cast-from-hand-only restriction"));
        }
        // CR 601.3 — named-card cast block (Meddling Mage: "spells with the
        // chosen name can't be cast"). Reject a cast when the spell's card
        // name is currently registered as blocked.
        if (action.Card != null
            && CastingRestrictions.IsCardNameBlocked(action.Card.Name))
        {
            return ValidationResult.Invalid(
                $"{action.Card.Name} can't be cast (Meddling Mage / named-card block)",
                new RuleViolation("601.3", "named-card cast restriction"));
        }

        // CR 702.11 / CR 113.5 — player-hexproof gate. When the cast
        // names one or more player targets, reject the cast if any
        // target is a player who has hexproof and isn't the caster.
        // Self-targeting (e.g. casting Healing Salve on yourself) is
        // explicitly allowed — hexproof only blocks spells controlled
        // by opponents.
        if (action.Targets != null && action.Player != null)
        {
            foreach (var target in action.Targets)
            {
                if (target is Player targetPlayer
                    && targetPlayer.HasHexproof
                    && !ReferenceEquals(targetPlayer, action.Player))
                {
                    return ValidationResult.Invalid(
                        $"{targetPlayer.Name} has hexproof",
                        new RuleViolation("702.11", "player-hexproof"));
                }
            }
        }

        return ValidationResult.Valid();
    }

    /// <summary>
    /// Validate activating an ability.
    /// </summary>
    private ValidationResult ValidateActivateAbility(ActivateAbilityAction action)
    {
        if (action == null || action.Ability == null)
        {
            return ValidationResult.Invalid("ActivateAbilityAction is missing an ability");
        }

        // CR 602.5c — name-targeted activated-ability suppression
        // (Pithing Needle, Phyrexian Revoker, Sorcerous Spyglass, …). When
        // a registered suppressor's chosen name matches this ability's
        // source name, reject the activation. CR 605 — mana abilities
        // are exempt; ActivatedAbilityRestrictions handles that filter
        // internally (and mana abilities take a separate activator path
        // anyway, so they don't reach ValidateActivateAbility).
        if (ActivatedAbilityRestrictions.IsActivatedAbilityRestricted(action.Ability))
        {
            var sourceName = (action.Ability.Source as Cards.ICard)?.Name ?? "<unknown>";
            return ValidationResult.Invalid(
                $"Activated abilities of {sourceName} can't be activated (chosen name)",
                new RuleViolation("602.5c", "name-targeted activated-ability suppression"));
        }

        // CR 117.1a / 307.5 — "Activate only as a sorcery" rider.
        // Sorcery-speed-only activations require the controller's main
        // phase with an empty stack. Caller marks the timing window via
        // SorcerySpeedAvailable, mirroring the spell-cast surface
        // (CastSpellAction). The validator stays stateless — it doesn't
        // introspect the game loop.
        if (action.Ability.IsSorcerySpeed && !action.SorcerySpeedAvailable)
        {
            var sourceName = (action.Ability.Source as Cards.ICard)?.Name ?? "<unknown>";
            return ValidationResult.Invalid(
                $"{sourceName}'s ability can only be activated as a sorcery",
                new RuleViolation("307.5", "activate-only-as-a-sorcery"));
        }

        // CR 702.11 / CR 113.5 — player-hexproof gate. When the
        // activation names one or more player targets, reject the
        // activation if any target is a player who has hexproof and
        // isn't the activator. Self-targeting (e.g. activating a
        // "you gain N life" ability on yourself) is explicitly allowed
        // — hexproof only blocks abilities controlled by opponents.
        if (action.Targets != null && action.Player != null)
        {
            foreach (var target in action.Targets)
            {
                if (target is Player targetPlayer
                    && targetPlayer.HasHexproof
                    && !ReferenceEquals(targetPlayer, action.Player))
                {
                    return ValidationResult.Invalid(
                        $"{targetPlayer.Name} has hexproof",
                        new RuleViolation("702.11", "player-hexproof"));
                }
            }
        }

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

    /// <summary>True when sorcery-speed timing is currently legal (CR
    /// 117.1a): active player's main phase + empty stack. Caller must
    /// supply; the validator doesn't introspect the game loop.</summary>
    public bool SorcerySpeedAvailable { get; }

    /// <summary>
    /// The zone the spell is being cast from (CR 601.2a). When set,
    /// external "can only cast from hand" restrictions (CR 113.6 —
    /// Drannith Magistrate) consult this to reject casts whose source
    /// zone isn't the hand. Null means "unspecified" — the validator
    /// treats unspecified casts as unrestricted on the from-zone axis
    /// for backward compatibility with the (huge) set of callers that
    /// don't yet stamp a source zone.
    /// </summary>
    public ZoneType? FromZone { get; }

    /// <summary>
    /// CR 115 / 601.2c — the targets chosen at cast time, in declaration
    /// order. Used by the <see cref="ActionValidator"/> player-hexproof
    /// gate (CR 702.11) to reject opponent-controlled spells naming a
    /// hexproof player. Null = unspecified (no target-axis validation —
    /// matches the legacy posture for the many callers that don't
    /// stamp targets). The validator only inspects entries that are
    /// <see cref="Player"/> instances; permanent / creature targets are
    /// still routed through
    /// <see cref="Majik.Core.Targeting.TargetLegality"/> at cast and at
    /// resolution time.
    /// </summary>
    public IReadOnlyList<object>? Targets { get; }

    public CastSpellAction(ICard card, Player player, bool sorcerySpeedAvailable = true)
        : this(card, player, sorcerySpeedAvailable, fromZone: null, targets: null)
    {
    }

    public CastSpellAction(ICard card, Player player, bool sorcerySpeedAvailable, ZoneType? fromZone)
        : this(card, player, sorcerySpeedAvailable, fromZone, targets: null)
    {
    }

    public CastSpellAction(
        ICard card,
        Player player,
        bool sorcerySpeedAvailable,
        ZoneType? fromZone,
        IReadOnlyList<object>? targets)
    {
        Card = card;
        Player = player;
        SorcerySpeedAvailable = sorcerySpeedAvailable;
        FromZone = fromZone;
        Targets = targets;
    }
}

/// <summary>
/// Action to activate an ability.
/// </summary>
public class ActivateAbilityAction : PlayerAction
{
    public IActivatedAbility Ability { get; }
    public Player Player { get; }

    /// <summary>
    /// True when sorcery-speed timing is currently legal (CR 117.1a /
    /// 307.5): the activating player's main phase + empty stack. Caller
    /// must supply when activating a sorcery-speed-only ability
    /// (<see cref="IActivatedAbility.IsSorcerySpeed"/>); the validator
    /// doesn't introspect the game loop. Mirrors
    /// <see cref="CastSpellAction.SorcerySpeedAvailable"/>. Defaults to
    /// true for backward compatibility with the (many) callers that
    /// don't yet stamp a timing window — instant-speed activations are
    /// unaffected regardless of this flag.
    /// </summary>
    public bool SorcerySpeedAvailable { get; }

    /// <summary>
    /// CR 115 / 602.1b — the targets chosen at activation time, in
    /// declaration order. Used by the <see cref="ActionValidator"/>
    /// player-hexproof gate (CR 702.11) to reject opponent-controlled
    /// activations naming a hexproof player. Null = unspecified — see
    /// <see cref="CastSpellAction.Targets"/> for the same posture.
    /// </summary>
    public IReadOnlyList<object>? Targets { get; }

    public ActivateAbilityAction(IActivatedAbility ability, Player player)
        : this(ability, player, sorcerySpeedAvailable: true, targets: null)
    {
    }

    public ActivateAbilityAction(IActivatedAbility ability, Player player, bool sorcerySpeedAvailable)
        : this(ability, player, sorcerySpeedAvailable, targets: null)
    {
    }

    public ActivateAbilityAction(
        IActivatedAbility ability,
        Player player,
        bool sorcerySpeedAvailable,
        IReadOnlyList<object>? targets)
    {
        Ability = ability;
        Player = player;
        SorcerySpeedAvailable = sorcerySpeedAvailable;
        Targets = targets;
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
