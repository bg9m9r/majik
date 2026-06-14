using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
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
        return CheckCastTimingGates(action)
            ?? CheckCastZoneGates(action)
            ?? CheckCastRestrictionGates(action)
            ?? CheckPlayerTargetGate(action.Player, action.Targets, action.Card)
            ?? CheckDeclaredTargetGate(action.Player, action.Targets, action.TargetSpec)
            ?? ValidationResult.Valid();
    }

    /// <summary>CR 117.1 / 302.1 / 117.1a — timing-axis cast gates: sorcery-
    /// speed restriction (intrinsic non-instant) AND external sorcery-speed
    /// restriction (Teferi, Time Raveler). Returns the first failure or
    /// null when both pass.</summary>
    private static ValidationResult? CheckCastTimingGates(CastSpellAction action)
    {
        // CR 117.1 / 302.1 — non-instant non-Flash cards need sorcery speed.
        if (!action.SorcerySpeedAvailable
            && !TimingRules.CanCastAtInstantSpeed(action.Card))
        {
            return ValidationResult.Invalid(
                $"{action.Card.Name} requires sorcery speed",
                new RuleViolation("117.1", "non-instant cast at non-sorcery speed"));
        }

        // CR 601.3 / 117.1a — external sorcery-speed restrictions (Teferi).
        if (!action.SorcerySpeedAvailable
            && action.Player != null
            && CastingRestrictions.MustCastAtSorcerySpeed(action.Player))
        {
            return ValidationResult.Invalid(
                $"{action.Player.Name} can cast spells only at sorcery speed",
                new RuleViolation("117.1a", "external sorcery-speed restriction"));
        }

        return null;
    }

    /// <summary>CR 601.2a / 117.6 / 113.6 / 601.3 — from-zone-axis cast
    /// gates: card-baked restricted zones (Hogaak), cast-from-hand-only
    /// player restrictions (Drannith Magistrate), and globally-blocked
    /// zones (Grafdigger's Cage).</summary>
    private static ValidationResult? CheckCastZoneGates(CastSpellAction action)
    {
        // CR 601.2a / 117.6 — card-baked restricted zones (Hogaak).
        if (action.Card is Card concreteCard
            && action.FromZone.HasValue
            && concreteCard.RestrictedCastZones.Contains(action.FromZone.Value))
        {
            return ValidationResult.Invalid(
                $"{action.Card.Name} can't be cast from {action.FromZone.Value}",
                new RuleViolation("601.2a", $"{action.Card.Name} can't be cast from {action.FromZone.Value}"));
        }

        // CR 113.6 / 601.3 — cast-from-hand-only player restriction
        // (Drannith Magistrate: opponents can only cast from their hands).
        if (action.Player != null
            && action.FromZone.HasValue
            && action.FromZone.Value != ZoneType.Hand
            && CastingRestrictions.MustCastFromHand(action.Player))
        {
            return ValidationResult.Invalid(
                $"{action.Player.Name} can't cast spells from {action.FromZone.Value}",
                new RuleViolation("113.6", "cast-from-hand-only restriction"));
        }

        // CR 601.3 — global cast-from-zone block (Grafdigger's Cage).
        if (action.FromZone.HasValue
            && CastingRestrictions.IsCastFromZoneGloballyBlocked(action.FromZone.Value))
        {
            return ValidationResult.Invalid(
                $"Players can't cast spells from {action.FromZone.Value}",
                new RuleViolation("601.3", $"global cast-from-zone block on {action.FromZone.Value}"));
        }

        return null;
    }

    /// <summary>CR 601.3 — registered casting-restriction gates: named-card
    /// block (Meddling Mage), per-player named-card block (Reflector Mage),
    /// total-cast block (Grand Abolisher), and noncreature-spell block
    /// (Ranger-Captain of Eos).</summary>
    private static ValidationResult? CheckCastRestrictionGates(CastSpellAction action)
    {
        // CR 601.3 — named-card cast block (Meddling Mage).
        if (action.Card != null
            && CastingRestrictions.IsCardNameBlocked(action.Card.Name))
        {
            return ValidationResult.Invalid(
                $"{action.Card.Name} can't be cast (Meddling Mage / named-card block)",
                new RuleViolation("601.3", "named-card cast restriction"));
        }

        // CR 601.3 — per-player named-card cast block (Reflector Mage).
        if (action.Card != null
            && action.Player != null
            && CastingRestrictions.IsCardNameBlockedForPlayer(action.Player, action.Card.Name))
        {
            return ValidationResult.Invalid(
                $"{action.Player.Name} can't cast {action.Card.Name} (Reflector Mage / per-player name block)",
                new RuleViolation("601.3", "per-player named-card cast restriction"));
        }

        // CR 601.3 — total cast block (Voice of Victory / Grand Abolisher).
        if (action.Player != null
            && CastingRestrictions.CannotCastAnySpell(action.Player))
        {
            return ValidationResult.Invalid(
                $"{action.Player.Name} can't cast spells right now",
                new RuleViolation("601.3", "total cast restriction"));
        }

        // CR 601.3 — even-mana-value cast block (Void Winnower: "Your
        // opponents can't cast spells with even mana values. (Zero is even.)").
        // Applies to EVERY spell type (creature and noncreature alike), unlike
        // the noncreature-only Sanctum Prelate rail. Mana value is computed as
        // printed MV + chosen X (CR 202.3b), and parity follows CR 202.3 —
        // zero is even. Rejected when the player is registered AND the
        // candidate spell's mana value is even.
        if (action.Card is Card evenMvCard
            && action.Player != null
            && CastingRestrictions.CannotCastEvenManaValueSpell(action.Player))
        {
            var manaValue = evenMvCard.ManaCostValue.TotalValue + (evenMvCard.PendingCastX ?? 0);
            if (manaValue % 2 == 0)
            {
                return ValidationResult.Invalid(
                    $"{action.Player.Name} can't cast spells with even mana values (mana value {manaValue}) (Void Winnower)",
                    new RuleViolation("601.3", "even-mana-value cast restriction"));
            }
        }

        // CR 601.3 — turn-scoped noncreature-spell restriction
        // (Ranger-Captain of Eos).
        if (action.Card != null
            && action.Player != null
            && !action.Card.HasType(Cards.Types.CardType.Creature)
            && CastingRestrictions.CannotCastNoncreatureSpell(action.Player))
        {
            return ValidationResult.Invalid(
                $"{action.Player.Name} can't cast noncreature spells this turn",
                new RuleViolation("601.3", "noncreature-spell restriction"));
        }

        // CR 601.3 — noncreature-spell mana-value block (Sanctum Prelate:
        // "Noncreature spells with mana value equal to the chosen number
        // can't be cast."). Gated to noncreature spells here; the registry
        // rail itself is mana-value-keyed and player-agnostic (symmetric).
        // Mana value is computed as printed MV + chosen X (CR 202.3b), the
        // same convention Chalice of the Void uses for its MV comparison.
        if (action.Card is Card mvCard
            && !mvCard.HasType(Cards.Types.CardType.Creature))
        {
            var manaValue = mvCard.ManaCostValue.TotalValue + (mvCard.PendingCastX ?? 0);
            if (CastingRestrictions.IsNoncreatureManaValueBlocked(manaValue))
            {
                return ValidationResult.Invalid(
                    $"Noncreature spells with mana value {manaValue} can't be cast (Sanctum Prelate)",
                    new RuleViolation("601.3", "noncreature mana-value cast restriction"));
            }

            // CR 601.3 — noncreature-spell "mana value N or greater" block
            // (Gaddock Teeg: "Noncreature spells with mana value 4 or greater
            // can't be cast."). Same noncreature gating + MV (printed MV +
            // chosen X, CR 202.3b) computation as the exact-value Sanctum
            // Prelate rail above; the registry rail tests the >= threshold.
            if (CastingRestrictions.IsNoncreatureManaValueAtLeastBlocked(manaValue))
            {
                return ValidationResult.Invalid(
                    $"Noncreature spells with mana value {manaValue} can't be cast (Gaddock Teeg / mana-value-or-greater block)",
                    new RuleViolation("601.3", "noncreature mana-value-or-greater cast restriction"));
            }

            // CR 601.3 — noncreature-spell "{X} in their mana costs" block
            // (Gaddock Teeg: "Noncreature spells with {X} in their mana costs
            // can't be cast."). Tests the printed cost for the {X} symbol
            // (CR 107.3 — Card.ManaCostValue.HasX), independent of the chosen X
            // value, and only for noncreature spells.
            if (mvCard.ManaCostValue.HasX
                && CastingRestrictions.IsNoncreatureXCostBlocked())
            {
                return ValidationResult.Invalid(
                    $"Noncreature spells with {{X}} in their mana costs can't be cast (Gaddock Teeg)",
                    new RuleViolation("601.3", "noncreature X-cost cast restriction"));
            }
        }

        // CR 601.3 — turn-scoped additional-spell cap (Irencrag Feat:
        // "You can cast only one more spell this turn."). Rejected when
        // the counter has been fully consumed.
        if (action.Player != null
            && CastingRestrictions.HasExhaustedAdditionalSpellAllowance(action.Player))
        {
            return ValidationResult.Invalid(
                $"{action.Player.Name} can't cast any more spells this turn (additional-spell cap reached)",
                new RuleViolation("601.3", "additional-spell cap exhausted"));
        }

        // CR 601.3 / 611 — static "can't cast more than N spells each turn" cap
        // (Eidolon of Rhetoric / Archon of Emeria). Distinct ledger from the
        // consumable Irencrag-Feat allowance above: this reads the explicit
        // per-turn spells-cast counter against the tightest registered static
        // cap and is never consumed.
        if (action.Player != null
            && CastingRestrictions.IsAtSpellsPerTurnCap(action.Player))
        {
            return ValidationResult.Invalid(
                $"{action.Player.Name} can't cast more spells this turn (spells-per-turn cap reached)",
                new RuleViolation("601.3", "spells-per-turn cap reached"));
        }

        // CR 605/616 / 601.3 — Ethersworn Canonist nonartifact restriction:
        // "Each player who has cast a nonartifact spell this turn can't cast
        // additional nonartifact spells." Gated to NONARTIFACT candidate spells
        // here (an artifact spell is always castable, even after a nonartifact
        // spell). The rail itself combines the battlefield-gated symmetric
        // active flag with the per-player "has already cast a nonartifact spell
        // this turn" counter.
        if (action.Card != null
            && action.Player != null
            && !action.Card.HasType(Cards.Types.CardType.Artifact)
            && CastingRestrictions.IsRestrictedByCanonistNonartifact(action.Player))
        {
            return ValidationResult.Invalid(
                $"{action.Player.Name} can't cast additional nonartifact spells this turn (Ethersworn Canonist)",
                new RuleViolation("601.3", "Canonist nonartifact-spell restriction"));
        }

        return null;
    }

    /// <summary>CR 702.11 / 702.16 / 702.18 / 113.5 — player-target
    /// untargetability gate. Reject when any player target:
    /// <list type="bullet">
    ///   <item>has SHROUD (CR 702.18) — can't be targeted at ALL, including
    ///         by its own controller (Solitary Confinement); OR</item>
    ///   <item>has HEXPROOF (CR 702.11) and isn't the source player —
    ///         opponents can't target it (Leyline of Sanctity); OR</item>
    ///   <item>has PROTECTION FROM the source's card type (CR 702.16) — a
    ///         spell/ability whose source is of that type can't target it
    ///         (Serra's Emissary). Independent of who controls the source.</item>
    /// </list>
    /// <paramref name="source"/> is the spell card / ability-source card whose
    /// card types drive the protection check; null skips the protection arm
    /// (hexproof / shroud are still enforced). Shared between
    /// <see cref="ValidateCastSpell"/> and
    /// <see cref="ValidateActivateAbility"/>.</summary>
    private static ValidationResult? CheckPlayerTargetGate(
        Player? sourcePlayer, IReadOnlyList<object>? targets, Cards.ICard? source)
    {
        if (targets == null || sourcePlayer == null) return null;
        foreach (var target in targets)
        {
            if (target is not Player targetPlayer) continue;

            // CR 702.18 — shroud blocks all targeting, even self-targeting.
            if (targetPlayer.HasShroud)
            {
                return ValidationResult.Invalid(
                    $"{targetPlayer.Name} has shroud",
                    new RuleViolation("702.18", "player-shroud"));
            }

            // CR 702.11 — hexproof only blocks opponents' targeting.
            if (targetPlayer.HasHexproof
                && !ReferenceEquals(targetPlayer, sourcePlayer))
            {
                return ValidationResult.Invalid(
                    $"{targetPlayer.Name} has hexproof",
                    new RuleViolation("702.11", "player-hexproof"));
            }

            // CR 702.16 — protection from the source's card type, from any
            // controller (Serra's Emissary's player half). A source that is
            // (say) an Instant can't target a player with "protection from
            // instants".
            if (source != null)
            {
                foreach (var type in source.CardTypes)
                {
                    if (targetPlayer.HasProtectionFromCardType(type))
                    {
                        return ValidationResult.Invalid(
                            $"{targetPlayer.Name} has protection from {type}",
                            new RuleViolation("702.16", "player-protection-from-card-type"));
                    }
                }
            }
        }
        return null;
    }

    /// <summary>CR 601.2c / 602.1b — declared-target-type legality gate.
    /// Reject the action at DECLARATION time when a chosen object is not a
    /// legal target for the declared <see cref="TargetSpec"/> — i.e. it fails
    /// the spec's type / zone / controller predicate (e.g. "target creature"
    /// pointed at a land, or at a creature not on the battlefield) OR it is
    /// rendered untargetable by an untargetability keyword (hexproof, shroud,
    /// protection — CR 702). Previously the engine relied on the
    /// resolution-time recheck (CR 608.2b) to fizzle an illegal target; this
    /// gate moves the check forward to declaration so an illegal target makes
    /// the action illegal up front (CR 601.2c), the same posture the Comp Rules
    /// require for choosing targets.
    ///
    /// <para>Builds on the existing <see cref="TargetLegality.IsLegal"/>
    /// predicate (already used for the resolution-time CR 608.2b recheck), so
    /// the validation-time and resolution-time legality definitions are one and
    /// the same. <paramref name="spec"/> is null for the (many) callers that
    /// don't yet stamp a declared spec — those keep the legacy
    /// resolution-only posture and are unaffected. Player-target
    /// untargetability is still independently covered by
    /// <see cref="CheckPlayerTargetGate"/>; this gate additionally type-filters
    /// every chosen object against the declared spec.</para></summary>
    private static ValidationResult? CheckDeclaredTargetGate(
        Player? caster, IReadOnlyList<object>? targets, TargetSpec? spec)
    {
        if (spec == null || targets == null || caster == null) return null;
        foreach (var target in targets)
        {
            if (!Majik.Core.Targeting.TargetLegality.IsLegal(spec, target, caster))
            {
                var label = (target as Cards.ICard)?.Name
                    ?? (target as Player)?.Name
                    ?? target?.GetType().Name
                    ?? "<null>";
                return ValidationResult.Invalid(
                    $"{label} is not a legal target for \"{spec.Description}\"",
                    new RuleViolation("601.2c", "illegal target at declaration"));
            }
        }
        return null;
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

        // CR 602.5c / 605.1a — "Activate only if <condition>" gate
        // (Metalcraft, Delirium, "you control a Forest", a hand-size check,
        // …). The ability carries a live predicate; reject the activation
        // when the condition is currently unsatisfied. Distinct from the
        // timing-only sorcery-speed rider below — this is a game-state gate
        // that holds at any speed.
        if (!action.Ability.CanActivateNow())
        {
            var sourceName = (action.Ability.Source as Cards.ICard)?.Name ?? "<unknown>";
            return ValidationResult.Invalid(
                $"{sourceName}'s ability: Activate only if its condition is met",
                new RuleViolation("602.5c", "activate-only-if-condition"));
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

        // CR 702.11 / 702.16 / 702.18 / 113.5 — player-target untargetability
        // gate (shared with cast path). The ability's source card drives the
        // protection-from-card-type arm.
        return CheckPlayerTargetGate(
                action.Player, action.Targets, action.Ability.Source as Cards.ICard)
            // CR 602.1b / 601.2c — declared-target-type legality (shared with
            // cast path); reject an illegal target at activation declaration.
            ?? CheckDeclaredTargetGate(action.Player, action.Targets, action.TargetSpec)
            ?? ValidationResult.Valid();
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
    /// stamp targets). <see cref="CheckPlayerTargetGate"/> inspects only the
    /// <see cref="Player"/> entries (hexproof / shroud / protection); when a
    /// declared <see cref="TargetSpec"/> is also supplied,
    /// <see cref="ActionValidator.CheckDeclaredTargetGate"/> additionally
    /// type-filters EVERY chosen object (permanent / creature targets included)
    /// against the spec through
    /// <see cref="Majik.Core.Targeting.TargetLegality"/> at declaration
    /// (CR 601.2c) — the same predicate the resolution-time CR 608.2b recheck
    /// uses.
    /// </summary>
    public IReadOnlyList<object>? Targets { get; }

    /// <summary>
    /// CR 601.2c — the declared target specification for this spell (the
    /// type / zone / controller predicate the chosen <see cref="Targets"/>
    /// must satisfy, e.g. "target creature" / "any target"). When supplied,
    /// the <see cref="ActionValidator"/> rejects the cast at DECLARATION if a
    /// chosen target fails the spec or is rendered untargetable
    /// (<see cref="Majik.Core.Targeting.TargetLegality"/>), rather than only
    /// fizzling at resolution (CR 608.2b). Null = unspecified — the validator
    /// keeps the legacy resolution-only posture for the many callers that
    /// don't yet stamp a declared spec.
    /// </summary>
    public Majik.Core.Targeting.TargetSpec? TargetSpec { get; }

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
        : this(card, player, sorcerySpeedAvailable, fromZone, targets, targetSpec: null)
    {
    }

    public CastSpellAction(
        ICard card,
        Player player,
        bool sorcerySpeedAvailable,
        ZoneType? fromZone,
        IReadOnlyList<object>? targets,
        Majik.Core.Targeting.TargetSpec? targetSpec)
    {
        Card = card;
        Player = player;
        SorcerySpeedAvailable = sorcerySpeedAvailable;
        FromZone = fromZone;
        Targets = targets;
        TargetSpec = targetSpec;
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

    /// <summary>
    /// CR 601.2c / 602.1b — the declared target specification for this
    /// activated ability. When supplied, the <see cref="ActionValidator"/>
    /// rejects the activation at DECLARATION if a chosen target fails the
    /// spec or is rendered untargetable, mirroring
    /// <see cref="CastSpellAction.TargetSpec"/>. Null = unspecified.
    /// </summary>
    public Majik.Core.Targeting.TargetSpec? TargetSpec { get; }

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
        : this(ability, player, sorcerySpeedAvailable, targets, targetSpec: null)
    {
    }

    public ActivateAbilityAction(
        IActivatedAbility ability,
        Player player,
        bool sorcerySpeedAvailable,
        IReadOnlyList<object>? targets,
        Majik.Core.Targeting.TargetSpec? targetSpec)
    {
        Ability = ability;
        Player = player;
        SorcerySpeedAvailable = sorcerySpeedAvailable;
        Targets = targets;
        TargetSpec = targetSpec;
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
