using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sword of Sinew and Steel (Modern Horizons 2,
/// {3}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +2/+2 and has protection from black and
///    from red."
///   "Whenever equipped creature deals combat damage to a player,
///    destroy up to one target planeswalker and up to one target
///    artifact."
///   "Equip {2}."
///
/// Same shape as <see cref="SwordOfFireAndIceFactory"/> /
/// <see cref="SwordOfFeastAndFamineFactory"/>: AttachedBoostEffect at
/// Layer 7c for +2/+2, GrantAbilityEffect re-projecting
/// <see cref="ProtectionAbility"/>("black") /
/// ProtectionAbility("red") onto the equipped creature at Layer 6, and a
/// <see cref="CombatDamageDealtEvent"/>-keyed
/// <see cref="TriggeredAbility"/> gated on the equipped creature dealing
/// damage to a player (CR 510 / CR 603.1).
///
/// ## Combat-damage rider
///
/// "Destroy up to one target planeswalker and up to one target artifact"
/// — two independent 0..1 target slots (CR 115.3 — "up to one target"
/// permits choosing zero or one). On resolution each chosen target is
/// routed through <see cref="Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/>
/// with <see cref="ZoneMoveReason.Destroy"/> so indestructible
/// (CR 702.12b) and regeneration (CR 701.15c) gates apply.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits service
/// wiring and produces the correct card shape only. With the runtime
/// overload, +2/+2 / protection grants register against
/// <paramref name="continuousEffects"/> and the trigger registers
/// against <paramref name="triggers"/>.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically.
/// - <b>Target prompts</b> for the combat trigger — v1 honours
///   pre-supplied targets via
///   <see cref="TriggeredAbility.SetChosenTargets"/>; absent supplied
///   targets each destroy half no-ops (CR 608.2b — do as much as
///   possible; "up to one" permits zero).
/// </summary>
[CardName("Sword of Sinew and Steel")]
public static class SwordOfSinewAndSteelFactory
{
    public const string CardName = "Sword of Sinew and Steel";
    public const string Cost = "{3}";
    public const string EquipCost = "{2}";

    /// <summary>
    /// Constructs Sword of Sinew and Steel with no live runtime wiring
    /// (shape / dispatcher path). Protection markers are present on the
    /// equipment card; the combat-damage trigger is attached for shape
    /// but not registered with a <see cref="TriggerManager"/>.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Constructs Sword of Sinew and Steel. When
    /// <paramref name="continuousEffects"/> is supplied the +2/+2 boost
    /// (Layer 7c) and protection grants (Layer 6, black + red) register
    /// against it. When <paramref name="triggers"/> is supplied the
    /// combat-damage trigger is registered so a
    /// <see cref="CombatDamageDealtEvent"/> from the equipped creature
    /// targeting a player automatically queues the ability.
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
        // Protection grants — "has protection from black and from red"
        // (CR 702.16 / CR 613.1f). Same wiring as Sword of Fire and Ice.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: _ => new ProtectionAbility("black")));
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: _ => new ProtectionAbility("red")));
        }
        else
        {
            card.AddAbility(new ProtectionAbility("black"));
            card.AddAbility(new ProtectionAbility("red"));
        }

        // --------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510 / CR 603.1.
        //   "Whenever equipped creature deals combat damage to a player,
        //    destroy up to one target planeswalker and up to one target
        //    artifact."
        // Two independent 0..1 target slots; resolution routes each
        // chosen permanent through Fx.MoveToGraveyard(Destroy) so
        // indestructible + regeneration gates apply.
        // --------------------------------------------------------------
        TriggeredAbility? combatTrigger = null;
        var combatEffect = new Effect(
            $"{CardName}: destroy up to one planeswalker + up to one artifact",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                if (combatTrigger == null) return;

                // Slot 0 — planeswalker. Slot 1 — artifact. Either slot
                // may be empty (CR 115.3 — "up to one target").
                var slots = combatTrigger.ChosenTargets;

                if (slots.Count > 0 && slots[0].Count > 0
                    && slots[0][0] is Permanent walker
                    && walker.HasType(CardType.Planeswalker))
                {
                    Fx.MoveToGraveyard(walker, ZoneMoveReason.Destroy);
                }

                if (slots.Count > 1 && slots[1].Count > 0
                    && slots[1][0] is Permanent artifact
                    && artifact.HasType(CardType.Artifact))
                {
                    Fx.MoveToGraveyard(artifact, ZoneMoveReason.Destroy);
                }
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
                    Description: "up to one target planeswalker",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
                new TargetRequest(
                    Description: "up to one target artifact",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
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
