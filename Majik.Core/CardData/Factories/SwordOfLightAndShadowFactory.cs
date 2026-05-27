using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sword of Light and Shadow (Darksteel, {3}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +2/+2 and has protection from white and from
///    black."
///   "Whenever equipped creature deals combat damage to a player, you gain
///    3 life and you may return up to one target creature card from your
///    graveyard to your hand."
///   "Equip {2}."
///
/// ## Implementation
///
/// - <b>Static "equipped creature gets +2/+2"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (CR 613 Layer 7c). The
///   effect reads <see cref="Permanent.AttachedTo"/> dynamically, so
///   re-equipping transfers the boost without re-registration. Mirrors
///   <see cref="SwordOfFireAndIceFactory"/>.
/// - <b>"Protection from white and from black"</b> — CR 702.16. With a
///   <see cref="ContinuousEffectsService"/> wired, two
///   <see cref="GrantAbilityEffect"/> instances (CR 613.1f, Layer 6)
///   re-project <see cref="ProtectionAbility"/>("white") /
///   <see cref="ProtectionAbility"/>("black") onto the live equipped
///   creature. Selectors read <see cref="Permanent.AttachedTo"/> at sync
///   time, so re-equipping transfers the protection automatically. The
///   shape-only constructor leaves both markers on the equipment card so
///   factory-shape / dispatch tests still get a deterministic answer.
/// - <b>Combat-damage-to-a-player trigger (CR 510, CR 603.1)</b> — fires
///   on a <see cref="CombatDamageDealtEvent"/> whose
///   <see cref="CombatDamageDealtEvent.Source"/> matches the equipped
///   creature AND whose
///   <see cref="DamageDealtEvent.TargetPlayer"/> is non-null. On
///   resolution:
///     1. controller gains 3 life (CR 119.3 — <see cref="Player.GainLife"/>);
///     2. up to one target creature card from controller's graveyard is
///        returned to controller's hand (the "may" + "up to one" rider —
///        target is optional, hence a 0..1 <see cref="TargetRequest"/>).
///   When no target is supplied (shape-only path or "no") the gain still
///   resolves while the bounce no-ops — CR 608.2b "do as much as possible"
///   on a paired effect.
/// - <b>Equip {2}</b> — activated ability (CR 702.6) via
///   <see cref="EquipActivatedAbility"/>. v1 picker is deterministic: the
///   first creature on the controller's battlefield.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests. The combat-damage trigger is attached
/// for shape but not registered with a <see cref="TriggerManager"/>; the
/// static +2/+2 boost is not registered against any
/// <see cref="ContinuousEffectsService"/>. Use the overload to wire
/// runtime services.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically.
/// - <b>Real graveyard-creature prompt</b> for the combat trigger — v1
///   honours pre-supplied targets via
///   <see cref="TriggeredAbility.SetChosenTargets"/>; absent a chosen
///   target the bounce no-ops while the life gain still resolves.
/// - <b>"You may" prompt</b> — the printed "may" makes the bounce
///   optional. v1 treats absence of a chosen target as "no", so callers
///   that want the bounce must populate the chosen target. The life-gain
///   half is mandatory and always resolves.
/// </summary>
[CardName("Sword of Light and Shadow")]
public static class SwordOfLightAndShadowFactory
{
    public const string CardName = "Sword of Light and Shadow";
    public const string Cost = "{3}";
    public const string EquipCost = "{2}";
    public const int LifeGain = 3;

    /// <summary>
    /// Constructs Sword of Light and Shadow with no live runtime wiring
    /// (shape / dispatcher path). The +2/+2 boost is not registered against
    /// any service; the combat-damage trigger is attached for shape but
    /// not registered with a <see cref="TriggerManager"/>. Protection
    /// markers are present on the equipment card.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Constructs Sword of Light and Shadow. When
    /// <paramref name="continuousEffects"/> is supplied the +2/+2 boost
    /// (Layer 7c) is registered against it and two Layer-6 protection
    /// grants re-project ProtectionAbility("white") /
    /// ProtectionAbility("black") onto the live equipped creature. When
    /// <paramref name="triggers"/> is supplied the combat-damage-to-a-
    /// player trigger is registered so a <see cref="CombatDamageDealtEvent"/>
    /// from the equipped creature (targeting a player) automatically
    /// queues the ability.
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
        // white and from black." (CR 702.16, CR 613.1f).
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
                abilityFactory: _ => new ProtectionAbility("black")));
        }
        else
        {
            card.AddAbility(new ProtectionAbility("white"));
            card.AddAbility(new ProtectionAbility("black"));
        }

        // --------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510 / CR 603.1.
        //   "Whenever equipped creature deals combat damage to a player,
        //    you gain 3 life and you may return up to one target creature
        //    card from your graveyard to your hand."
        // --------------------------------------------------------------
        TriggeredAbility? combatTrigger = null;
        var combatEffect = new Effect(
            $"{CardName}: gain 3 life and may return target creature from graveyard",
            () =>
            {
                var controller = card.Controller ?? owner;

                // 1) Gain 3 life (CR 119.3). Mandatory half.
                controller.GainLife(LifeGain);

                // 2) Return up to one target creature card from
                //    controller's graveyard to hand (CR 608.2b "do as
                //    much as possible"). No target supplied → clean
                //    no-op (the "may" + "up to one" rider).
                if (combatTrigger == null
                    || combatTrigger.ChosenTargets.Count == 0
                    || combatTrigger.ChosenTargets[0].Count == 0) return;

                if (combatTrigger.ChosenTargets[0][0] is not ICard picked) return;

                // CR 608.2b illegal-on-resolution — must still be in
                // controller's graveyard and be a creature card.
                if (picked.Zone != ZoneType.Graveyard) return;
                if (!controller.Zones.Graveyard.GetCards().Contains(picked)) return;
                if (!picked.HasType(CardType.Creature)) return;

                controller.Zones.Graveyard.RemoveCard(picked);
                controller.Zones.Hand.AddCard(picked);
                picked.SetZone(ZoneType.Hand);
            });

        combatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (e.TargetPlayer == null) return false;
                var equipped = card.AttachedTo;
                if (equipped == null) return false;
                return ReferenceEquals(e.Source, equipped);
            }),
            effects: new IEffect[] { combatEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature card from your graveyard",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: owner.Zones.Graveyard.GetCards()
                        .Where(c => c.HasType(CardType.Creature))
                        .Cast<object>()
                        .ToList()),
            });

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
