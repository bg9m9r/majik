using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hammer of Nazahn (Commander 2017, {4}).
///
/// Legendary Artifact — Equipment. Oracle text:
///   "Equipped creature gets +2/+0 and has indestructible."
///   "Whenever an Equipment enters the battlefield under your control,
///    you may attach it to target creature you control."
///   "Equip {3}."
///
/// ## Implementation
///
/// - <b>Legendary supertype + Equipment subtype</b>, mana cost {4}.
/// - <b>Static "equipped creature gets +2/+0 and has indestructible"</b>:
///     - +2/+0 P/T boost (Layer 7c) via <see cref="AttachedBoostEffect"/>
///       — mirrors <see cref="SwordOfFireAndIceFactory"/>'s P/T grant.
///     - Indestructible grant (Layer 6) via
///       <see cref="GrantAbilityEffect"/> projecting a fresh
///       <see cref="KeywordAbility"/>("Indestructible") onto the live
///       equipped creature on each layer pass / re-equip — same shape as
///       Sword of Fire and Ice's protection grants.
///       <see cref="Majik.Core.Combat.CombatAbilities.HasIndestructible"/>
///       reads the marker off the bearer (and falls back to the
///       equipment card itself on the shape-only path).
/// - <b>Triggered ability: Equipment ETB under your control → may
///   attach</b> (CR 603.1, 603.6a, 702.6):
///     - Fires on <see cref="CardMovedEvent"/> to
///       <see cref="ZoneType.Battlefield"/> filtered to (Equipment subtype
///       + controller-matches). Same filter shape as
///       <see cref="PuresteelPaladinFactory"/>'s ETB-draw trigger but with
///       Hammer's own ETB included (printed wording has no "another"
///       carve-out — Hammer's own ETB triggers itself).
///     - Resolution: attach the equipment that entered to a chosen
///       creature the controller controls. v1 auto-accepts the "may" and
///       picks the first controller-side creature deterministically
///       (same posture as Cori-Steel Cutter's auto-attach to its own
///       token). When an agent supplies a target via
///       <see cref="TriggeredAbility.SetChosenTargets"/> it is honoured.
///     - The Equipment that triggered is captured via the trigger's
///       <see cref="EventTriggerCondition{TEvent}"/> match payload and
///       passed to the resolver closure through a per-trigger ref.
/// - <b>Equip {3}</b>: standard equipment-cycle
///   <see cref="EquipActivatedAbility"/> wiring with the Puresteel
///   zero-cost provider hook (CR 702.6).
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied, the +2/+0
/// boost and the Indestructible grant are both registered immediately;
/// each gates on Hammer being on the battlefield AND attached to a
/// battlefield permanent. When <paramref name="triggers"/> is supplied,
/// the Equipment-ETB trigger is registered so a <see cref="CardMovedEvent"/>
/// to Battlefield with the right filter auto-queues the ability.
///
/// The single-arg <see cref="Create(Player)"/> overload omits service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for the ETB trigger and the Equip
///   activation — v1 picks the first controller-side creature
///   deterministically (same gap as the rest of the equipment cycle).
/// - <b>Live "may" prompt</b> — v1 auto-accepts; same simplification as
///   Puresteel Paladin's ETB-draw "may" + Cori-Steel Cutter's auto-attach.
/// </summary>
[CardName("Hammer of Nazahn")]
public static class HammerOfNazahnFactory
{
    public const string CardName = "Hammer of Nazahn";
    public const string Cost = "{4}";
    public const string EquipCost = "{3}";

    /// <summary>
    /// Constructs Hammer of Nazahn with no live continuous-effects or
    /// trigger-manager wiring (the shape / dispatcher path). The boost is
    /// not registered; the Indestructible marker is added directly to the
    /// Hammer card so <see cref="Majik.Core.Combat.CombatAbilities.HasIndestructible"/>
    /// returns a deterministic answer for factory-shape tests. The ETB
    /// trigger is attached to the card but not registered with a
    /// <see cref="TriggerManager"/>.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Constructs Hammer of Nazahn. When
    /// <paramref name="continuousEffects"/> is supplied, the +2/+0 boost
    /// (Layer 7c) and Indestructible grant (Layer 6) are registered
    /// against it. When <paramref name="triggers"/> is supplied, the
    /// Equipment-ETB trigger is registered for bus-driven firing.
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
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Static "+2/+0" — CR 613 Layer 7c. Gates on attached (see
        // AttachedBoostEffect.IsActive).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 2, toughness: 0));
        }

        // --------------------------------------------------------------
        // "Equipped creature has indestructible" — CR 702.12 marker +
        // CR 613.1f Layer 6 grant. With a ContinuousEffectsService wired,
        // GrantAbilityEffect re-projects a fresh KeywordAbility
        // ("Indestructible") onto the live equipped creature; the marker
        // is what CombatAbilities.HasIndestructible reads. Shape-only
        // path falls back to leaving the Indestructible marker on the
        // hammer itself (analog to Sword of Fire and Ice's
        // protection-on-the-card fallback) so factory-shape tests still
        // observe the keyword somewhere on the equipment.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: bearer => new KeywordAbility("Indestructible", bearer, bearer.Controller ?? owner)));
        }
        else
        {
            card.AddAbility(new KeywordAbility("Indestructible", card, owner));
        }

        // --------------------------------------------------------------
        // Triggered ability — "Whenever an Equipment enters the
        // battlefield under your control, you may attach it to target
        // creature you control." (CR 603.1 / CR 603.6a / CR 702.6)
        //
        // Filter mirrors PuresteelPaladinFactory's ETB-draw trigger
        // (Artifact + Equipment + controller-matches). Includes Hammer
        // itself — printed wording has no "another" carve-out.
        //
        // The closure captures the entering Equipment via the trigger
        // condition's event match — at resolution time we re-derive it
        // from the most recent EquipmentEnteredUnderControl event stamp
        // (slot the trigger writes during its predicate). Mirrors the
        // pattern Sword of Fire and Ice uses for the combat trigger.
        // --------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        // Captures the live entering Equipment between predicate evaluation
        // and effect resolution — predicate stores the card, effect reads it.
        // (TriggeredAbility doesn't expose the matched event payload to the
        // effect, so a closure ref is the established workaround — same
        // shape as Bridge from Below's zombie-token capture.)
        ICard? enteringEquipment = null;

        var etbCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (!e.Card.HasType(CardType.Artifact)) return false;
            if (!e.Card.HasSubtype(CardSubtype.Equipment)) return false;
            if (!ReferenceEquals(e.Card.Controller, owner)) return false;

            enteringEquipment = e.Card;
            return true;
        });

        var etbEffect = new Effect(
            $"{CardName}: equipment entered — may attach to target creature you control",
            () =>
            {
                if (enteringEquipment is not Permanent equip) return;

                var ctrl = equip.Controller ?? owner;

                // Honour any agent-supplied target; else deterministic
                // first-creature pick (same posture as the rest of the
                // equipment cycle's v1 attach).
                Creature? bearer = null;
                if (etbTrigger != null
                    && etbTrigger.ChosenTargets.Count > 0
                    && etbTrigger.ChosenTargets[0].Count > 0
                    && etbTrigger.ChosenTargets[0][0] is Creature chosen
                    && ReferenceEquals(chosen.Controller, ctrl))
                {
                    bearer = chosen;
                }

                bearer ??= ctrl.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .FirstOrDefault(c => ReferenceEquals(c.Controller, ctrl));

                if (bearer == null) return; // CR 608.2b — no legal target → no-op.
                equip.AttachTo(bearer);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    CandidateGatherer: _ =>
                        owner.Zones.Battlefield.GetCards()
                            .OfType<Creature>()
                            .Where(c => ReferenceEquals(c.Controller, owner))
                            .Cast<object>()
                            .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // --------------------------------------------------------------
        // Equip {3} — activated ability (CR 702.6) via the shared
        // EquipActivatedAbility primitive. Threads the Puresteel
        // zero-cost provider hook.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
