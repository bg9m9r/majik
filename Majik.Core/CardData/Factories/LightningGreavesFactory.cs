using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lightning Greaves (Mirrodin, {2}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-06-02):
///   "Equipped creature has haste and shroud. (It can't be the target of
///    spells or abilities.)"
///   "Equip {0}"
///
/// The iconic protect-your-commander Equipment: zero equip cost, instant
/// haste, and shroud to dodge removal / targeted abilities. The shroud is
/// the catch — your own targeted spells and abilities can't hit the bearer
/// either (CR 702.18).
///
/// ## Why a hand-rolled C# factory (not the JSON CardDefinition path)
///
/// The data-driven
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> only
/// supports the effect/ability shapes enumerated in its dispatch (counters,
/// draw, scry/surveil, stub damage, …). It has NO equip ability and NO
/// attached keyword-grant — a JSON def alone produces only a vanilla Artifact
/// shell (the shipped <c>lightning-greaves.json</c> mirrors
/// <c>lavaspur-boots.json</c> / <c>nettlecyst.json</c>, which carry zero
/// abilities). The functioning equipment analogues
/// (<see cref="LavaspurBootsFactory"/>, <see cref="ColossusHammerFactory"/>,
/// <see cref="BonesplitterFactory"/>) are themselves hand-rolled for exactly
/// this reason, so Lightning Greaves follows that established pattern.
///
/// ## Implementation
///
/// - <b>"has haste"</b> (CR 702.10) — a Layer-6 <see cref="GrantAbilityEffect"/>
///   (CR 613.1f) re-projecting a fresh <see cref="KeywordAbility"/>("Haste")
///   onto the live equipped creature. The selector reads
///   <see cref="Permanent.AttachedTo"/> at sync time, so re-equipping
///   transfers the grant; LTB / detach revoke it via the service's grant
///   lifecycle. <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/>
///   reads the granted marker through the computed keyword set. Mirrors
///   <see cref="LavaspurBootsFactory"/>.
/// - <b>"has shroud"</b> (CR 702.18) — a Layer-6
///   <see cref="GrantAbilityEffect"/> projecting a
///   <see cref="KeywordAbility"/>("Shroud") onto the equipped creature.
///   Shroud is enforced by <see cref="Majik.Core.Targeting.TargetLegality"/>,
///   which reads "Shroud" off the bearer's computed keyword set and rejects
///   it as a target for ANY spell or ability (CR 702.18) — including the
///   controller's own.
/// - <b>Equip {0}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive, threading the
///   Puresteel-Paladin zero-equip cost-provider hook for cycle parity.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only (factory-shape / dispatch
/// tests). The haste / shroud grants are not registered against any
/// <see cref="ContinuousEffectsService"/> on that path. Use the two-arg
/// overload to wire the continuous effects; each grant gates on the Greaves
/// being on the battlefield AND attached to a battlefield permanent.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically (same gap as the rest of the
///   equipment cycle).
/// </summary>
[CardName("Lightning Greaves")]
public static class LightningGreavesFactory
{
    public const string CardName = "Lightning Greaves";
    public const string Cost = "{2}";

    /// <summary>CR 702.6 — printed equip cost: {0}.</summary>
    public const string EquipCost = "{0}";

    /// <summary>
    /// Constructs Lightning Greaves with no live continuous-effects wiring
    /// (the shape / dispatcher path). Neither the haste nor shroud grants are
    /// registered against any service.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs Lightning Greaves. When <paramref name="continuousEffects"/>
    /// is supplied the haste (CR 702.10) and shroud (CR 702.18) grants
    /// (Layer 6) are registered against it; each gates on the Greaves being on
    /// the battlefield AND attached to a battlefield permanent. When null,
    /// both are skipped.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // "Equipped creature has haste and shroud."
        // Both gate on the source being on the battlefield AND attached
        // (GrantAbilityEffect selector reads AttachedTo). CR 613.1f, Layer 6.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            // CR 702.10 — grant Haste.
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: bearer =>
                    new KeywordAbility("Haste", bearer, bearer.Controller ?? owner)));

            // CR 702.18 — grant Shroud. TargetLegality reads "Shroud" off the
            // bearer's computed keyword set and rejects it as a target for any
            // spell or ability, including the controller's own.
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: bearer =>
                    new KeywordAbility("Shroud", bearer, bearer.Controller ?? owner)));
        }

        // --------------------------------------------------------------
        // Equip {0} — standard equipment-cycle Equip activated ability
        // (CR 702.6) via the shared primitive. Threads the Puresteel
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
