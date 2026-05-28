using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Skateboard (The Brothers' War, {1}).
///
/// Artifact — Equipment. Oracle text:
///   "When this Equipment enters, tap target permanent."
///   "Equipped creature gets +1/+0 and has haste."
///   "Equip {1}."
///
/// ## Implementation
///
/// - <b>ETB trigger: "When this Equipment enters, tap target permanent."</b>
///   Wired as a <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. Declares a 1..1
///   "target permanent" <see cref="TargetRequest"/>. At resolution the
///   chosen permanent is tapped (CR 701.20 / CR 603.6a). If no legal
///   target was supplied the effect is a clean no-op (CR 608.2b).
///   The tap is unconditional — Skateboard's ETB text has no "may" rider.
///
/// - <b>Static "+1/+0" (Layer 7c, CR 613 Layer 7c)</b> — registered via
///   <see cref="AttachedBoostEffect"/> mirroring
///   <see cref="ColossusHammerFactory"/> / <see cref="HammerOfNazahnFactory"/>.
///   The effect reads <see cref="Permanent.AttachedTo"/> dynamically, so
///   re-equipping transfers the boost without re-registration.
///
/// - <b>Haste grant (Layer 6, CR 702.10 / CR 613.1f)</b> — registered via
///   <see cref="GrantAbilityEffect"/> projecting a fresh
///   <see cref="KeywordAbility"/>("Haste") onto the live equipped creature
///   on each layer pass / re-equip (same shape as
///   <see cref="BatterskullFactory"/>'s Vigilance + Lifelink grants and
///   <see cref="HammerOfNazahnFactory"/>'s Indestructible grant).
///   <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/> reads the
///   keyword marker off the bearer; summoning-sickness is bypassed while
///   the grant is live (CR 702.10a).
///   Shape-only path (no <see cref="ContinuousEffectsService"/> supplied):
///   the "Haste" marker is stamped on the Skateboard card itself so
///   factory-shape / dispatch tests can observe the keyword somewhere on
///   the equipment (same fallback as Hammer of Nazahn's Indestructible).
///
/// - <b>Equip {1}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive with the Puresteel-Paladin
///   zero-equip cost-provider hook.
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied, the +1/+0 boost
/// and the Haste grant are both registered immediately; each gates on
/// Skateboard being on the battlefield AND attached to a battlefield
/// permanent. When <paramref name="triggers"/> is supplied, the ETB tap
/// trigger is registered for bus-driven firing.
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests.
///
/// ## Deferred
///
/// - <b>Tap-target prompt</b> for the ETB trigger — v1 picks the target via
///   the standard <see cref="TargetRequest"/> / agent-prompt pipeline (same
///   posture as Pestermite / Deceiver Exarch). When no agent supplies a
///   target, the effect is a no-op per CR 608.2b.
/// </summary>
[CardName("Skateboard")]
public static class SkateboardFactory
{
    public const string CardName = "Skateboard";
    public const string Cost = "{1}";
    public const string EquipCost = "{1}";

    /// <summary>
    /// Constructs Skateboard with no live service wiring (the shape /
    /// dispatcher path). The ETB trigger is attached structurally but not
    /// registered with a <see cref="TriggerManager"/>; the boost and Haste
    /// grant are not registered against any
    /// <see cref="ContinuousEffectsService"/>; the "Haste" marker is added
    /// directly to the Skateboard card so factory-shape tests can observe it.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Constructs Skateboard. When <paramref name="continuousEffects"/> is
    /// supplied, the static +1/+0 boost (Layer 7c) and the Haste grant
    /// (Layer 6) are registered against it. When <paramref name="triggers"/>
    /// is supplied, the ETB tap trigger is registered for bus-driven firing.
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
        // Static "+1/+0" — CR 613 Layer 7c. Gates on Skateboard being on
        // the battlefield AND attached (see AttachedBoostEffect.IsActive).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 1, toughness: 0));
        }

        // --------------------------------------------------------------
        // "Equipped creature has haste" — CR 702.10 marker +
        // CR 613.1f Layer 6 grant. With a ContinuousEffectsService wired,
        // GrantAbilityEffect re-projects a fresh KeywordAbility("Haste")
        // onto the live equipped creature; the marker is what
        // CombatAbilities.HasHaste reads (CR 302.6 summoning-sickness
        // bypass). Shape-only path falls back to leaving the Haste marker
        // on the Skateboard card itself so factory-shape tests still
        // observe the keyword somewhere on the equipment.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: bearer => new KeywordAbility(
                    "Haste", bearer, bearer.Controller ?? owner)));
        }
        else
        {
            card.AddAbility(new KeywordAbility("Haste", card, owner));
        }

        // --------------------------------------------------------------
        // ETB trigger — "When this Equipment enters, tap target permanent."
        // CR 603.6a — triggers on the Skateboard entering the battlefield.
        // Declares a 1..1 "target permanent" TargetRequest; at resolution
        // taps the chosen permanent (CR 701.20). No "may" rider — the tap
        // is mandatory when a legal target was chosen (CR 608.2b covers
        // the illegal-at-resolution case).
        // --------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbEffect = new Effect(
            $"{CardName}: ETB — tap target permanent",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return; // no target chosen

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — target must still be on the battlefield at
                // resolution; if it has left, the effect is a no-op.
                if (target.Zone != ZoneType.Battlefield) return;

                target.Tap();
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // --------------------------------------------------------------
        // Equip {1} — activated ability (CR 702.6) via the shared
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
