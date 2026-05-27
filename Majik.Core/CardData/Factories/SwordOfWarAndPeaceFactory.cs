using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sword of War and Peace (New Phyrexia, {3}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +2/+2 and has protection from white and from
///    red."
///   "Whenever equipped creature deals combat damage to a player, Sword of
///    War and Peace deals damage to that player equal to the number of
///    cards in their hand and you gain 1 life for each card in your hand."
///   "Equip {2}."
///
/// ## Implementation
///
/// - <b>Static "equipped creature gets +2/+2"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (CR 613 Layer 7c).
/// - <b>"Protection from white and from red"</b> — CR 702.16. With a
///   <see cref="ContinuousEffectsService"/> wired, two
///   <see cref="GrantAbilityEffect"/> instances re-project
///   <see cref="ProtectionAbility"/>("white") /
///   <see cref="ProtectionAbility"/>("red") onto the live equipped
///   creature.
/// - <b>Combat-damage-to-a-player trigger (CR 510, CR 603.1)</b> — fires
///   on equipped-creature damage to a player. On resolution:
///     1. the Sword deals damage to the damaged player equal to that
///        player's hand size at resolution (CR 119.3 — read at the time
///        the ability resolves, NOT at fire time);
///     2. controller gains 1 life for each card in CONTROLLER's hand at
///        resolution. Two independent counts: the printed "their hand"
///        and "your hand" are different players in general.
///   Hand-size reads at resolve time mirror Fury's count-at-resolution
///   posture (see FuryFactory).
/// - <b>Equip {2}</b> — activated ability (CR 702.6) via
///   <see cref="EquipActivatedAbility"/>.
///
/// ## Lifecycle
///
/// Single-arg overload omits service wiring (shape-only). Runtime
/// overload wires +2/+2 + protection grants into the supplied
/// <see cref="ContinuousEffectsService"/> and registers the combat-
/// damage trigger against the supplied <see cref="TriggerManager"/>.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 deterministic first
///   controller-side creature.
/// </summary>
[CardName("Sword of War and Peace")]
public static class SwordOfWarAndPeaceFactory
{
    public const string CardName = "Sword of War and Peace";
    public const string Cost = "{3}";
    public const string EquipCost = "{2}";

    /// <summary>
    /// Constructs Sword of War and Peace with no live runtime wiring
    /// (shape / dispatcher path).
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Constructs Sword of War and Peace. When
    /// <paramref name="continuousEffects"/> is supplied the +2/+2 boost
    /// (Layer 7c) is registered and two Layer-6 protection grants
    /// re-project ProtectionAbility("white") / ProtectionAbility("red")
    /// onto the live equipped creature. When <paramref name="triggers"/>
    /// is supplied the combat-damage-to-a-player trigger is registered.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Static continuous effect — "Equipped creature gets +2/+2."
        // CR 613 Layer 7c.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 2, toughness: 2));
        }

        // --------------------------------------------------------------
        // Protection grants — "Equipped creature has protection from
        // white and from red." (CR 702.16, CR 613.1f).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: _ => new ProtectionAbility("white")));
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: _ => new ProtectionAbility("red")));
        }
        else
        {
            card.AddAbility(new ProtectionAbility("white"));
            card.AddAbility(new ProtectionAbility("red"));
        }

        // --------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510 / CR 603.1.
        //   "Whenever equipped creature deals combat damage to a player,
        //    Sword of War and Peace deals damage to that player equal to
        //    the number of cards in their hand and you gain 1 life for
        //    each card in your hand."
        //
        // Captures the damaged player off the event so the resolved
        // effect targets the correct hand at fire time. CR 119.3 — both
        // hand sizes are read at RESOLUTION time, not at fire time.
        // --------------------------------------------------------------
        Player? capturedDamaged = null;

        var combatEffect = new Effect(
            $"{CardName}: deal damage = damaged player's hand size + gain life = your hand size",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;

                var controller = card.Controller ?? owner;

                // 1) Damage to the damaged player = THEIR hand size at
                //    resolution (CR 119.3). Use LoseLife so it tracks as
                //    damage from the Sword (a noncombat damage source).
                var victim = capturedDamaged;
                if (victim != null)
                {
                    var damage = victim.Zones.Hand.GetCards().Count();
                    if (damage > 0)
                    {
                        victim.LoseLife(damage);
                    }
                }

                // 2) Controller gains 1 life for each card in CONTROLLER's
                //    hand at resolution.
                var lifeGain = controller.Zones.Hand.GetCards().Count();
                if (lifeGain > 0)
                {
                    controller.GainLife(lifeGain);
                }
            });

        var combatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                var equipped = card.AttachedTo;
                if (equipped == null) return false;
                if (!ReferenceEquals(e.Source, equipped)) return false;
                if (e.TargetPlayer == null) return false; // damage to a player only
                capturedDamaged = e.TargetPlayer;
                return true;
            }),
            effects: new IEffect[] { combatEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(combatTrigger);
        triggers?.RegisterTriggeredAbility(combatTrigger);

        // --------------------------------------------------------------
        // Equip {2} — activated ability (CR 702.6).
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
