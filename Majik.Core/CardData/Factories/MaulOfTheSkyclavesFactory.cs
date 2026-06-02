using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Maul of the Skyclaves (Zendikar Rising, {2}{W}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-06-02):
///   "When this Equipment enters, attach it to target creature you control."
///   "Equipped creature gets +2/+2 and has flying and first strike."
///   "Equip {2}{W}{W}"
///
/// ## Why a hand-rolled C# factory (not the JSON CardDefinition path)
///
/// The data-driven
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> only
/// supports the effect/ability shapes enumerated in its dispatch (counters,
/// draw, scry/surveil, stub damage, …). It has NO equip ability, NO dynamic
/// attached-boost effect, NO attached keyword-grant, and NO Equipment-ETB
/// attach trigger — a JSON def alone produces only a vanilla Artifact shell.
/// The shipped <c>maul-of-the-skyclaves.json</c> mirrors
/// <c>lavaspur-boots.json</c> / <c>hammer-of-nazahn.json</c>: name + types +
/// subtypes + cost only. The functioning behaviour is hand-rolled here, the
/// established pattern across the equipment cycle
/// (<see cref="HammerOfNazahnFactory"/>, <see cref="SwordOfFireAndIceFactory"/>,
/// <see cref="LavaspurBootsFactory"/>).
///
/// ## Implementation
///
/// - <b>Static "equipped creature gets +2/+2"</b> — <see cref="AttachedBoostEffect"/>
///   at Layer 7c (CR 613 Layer 7c). The effect reads
///   <see cref="Permanent.AttachedTo"/> dynamically, so re-equipping transfers
///   the boost without re-registration. Mirrors
///   <see cref="SwordOfFireAndIceFactory"/> / <see cref="LavaspurBootsFactory"/>.
/// - <b>"has flying and first strike"</b> (CR 702.9 / CR 702.7) — two Layer-6
///   <see cref="GrantAbilityEffect"/> instances (CR 613.1f) re-projecting fresh
///   <see cref="KeywordAbility"/>("Flying") / ("First strike") onto the live
///   equipped creature. The selectors read <see cref="Permanent.AttachedTo"/>
///   at sync time, so re-equipping transfers the grants; detach / LTB revoke
///   them via the service's grant lifecycle.
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> /
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/> read the
///   granted markers through the computed keyword set.
/// - <b>Triggered ability: "When this Equipment enters, attach it to target
///   creature you control."</b> (CR 603.1 / CR 603.6a / CR 702.6) — fires on
///   the Maul's own <see cref="CardMovedEvent"/> to
///   <see cref="ZoneType.Battlefield"/>. Same shape as
///   <see cref="HammerOfNazahnFactory"/>'s Equipment-ETB trigger, but scoped to
///   THIS card's own ETB (printed "this Equipment", not Hammer's "an Equipment").
///   Resolution: attach the Maul to a chosen creature the controller controls
///   (agent-supplied target honoured, else deterministic first-creature pick —
///   same v1 posture as the rest of the equipment cycle's auto-attach).
///   Note this is NOT a "may" — the trigger attaches if there is a legal
///   target (CR 608.2b — no legal creature → no-op).
/// - <b>Equip {2}{W}{W}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive, threading the
///   Puresteel-Paladin zero-equip cost-provider hook for cycle parity.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only (factory-shape / dispatch
/// tests). On that path the boost / flying / first-strike grants are not
/// registered, and the ETB trigger is attached to the card but not registered
/// with a <see cref="TriggerManager"/>. Use the three-arg overload to wire the
/// continuous effects and the trigger manager.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for the ETB trigger and Equip — v1 picks the
///   first controller-side creature deterministically (same gap as the rest of
///   the equipment cycle).
/// </summary>
[CardName("Maul of the Skyclaves")]
public static class MaulOfTheSkyclavesFactory
{
    public const string CardName = "Maul of the Skyclaves";
    public const string Cost = "{2}{W}";
    public const string EquipCost = "{2}{W}{W}";

    /// <summary>
    /// Constructs Maul of the Skyclaves with no live continuous-effects or
    /// trigger-manager wiring (the shape / dispatcher path). The +2/+2 boost
    /// and flying / first-strike grants are not registered; the ETB trigger is
    /// attached to the card but not registered with a
    /// <see cref="TriggerManager"/>.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Constructs Maul of the Skyclaves. When
    /// <paramref name="continuousEffects"/> is supplied the static +2/+2 boost
    /// (Layer 7c) and the flying / first-strike grants (Layer 6) are registered
    /// against it; each gates on the Maul being on the battlefield AND attached
    /// to a battlefield permanent. When <paramref name="triggers"/> is supplied
    /// the Equipment-ETB attach trigger is registered so the Maul's own
    /// <see cref="CardMovedEvent"/> to Battlefield auto-queues the ability.
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
        // "Equipped creature gets +2/+2 and has flying and first strike."
        // All three gate on the source being on the battlefield AND
        // attached (effect IsActive checks / GrantAbilityEffect selector).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            // CR 613 Layer 7c — P/T modification.
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 2, toughness: 2));

            // CR 702.9 — grant Flying (CR 613.1f, Layer 6 ability-adding).
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: bearer =>
                    new KeywordAbility("Flying", bearer, bearer.Controller ?? owner)));

            // CR 702.7 — grant First strike (CR 613.1f, Layer 6). Keyword
            // string is "First strike" to match CombatAbilities.HasFirstStrike.
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: bearer =>
                    new KeywordAbility("First strike", bearer, bearer.Controller ?? owner)));
        }

        // --------------------------------------------------------------
        // Triggered ability — "When this Equipment enters, attach it to
        // target creature you control." (CR 603.1 / CR 603.6a / CR 702.6)
        //
        // Fires on THIS card's own ETB (CardMovedEvent to Battlefield).
        // Same machinery as HammerOfNazahnFactory's Equipment-ETB trigger,
        // scoped to the Maul itself rather than any Equipment. This is not a
        // "may" — the Maul attaches if a legal target exists.
        // --------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            return ReferenceEquals(e.Card, card);
        });

        var etbEffect = new Effect(
            $"{CardName}: this Equipment entered — attach to target creature you control",
            () =>
            {
                var ctrl = card.Controller ?? owner;

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
                card.AttachTo(bearer);
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
        // Equip {2}{W}{W} — activated ability (CR 702.6) via the shared
        // EquipActivatedAbility primitive. Threads the Puresteel zero-cost
        // provider hook for cycle parity.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
