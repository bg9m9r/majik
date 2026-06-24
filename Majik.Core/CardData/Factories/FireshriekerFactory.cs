using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fireshrieker (Mirrodin / Eighth Edition, {3}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-06-24):
///   "Equipped creature has double strike. (It deals both first-strike and
///    regular combat damage.)"
///   "Equip {2}"
///
/// ## Why a hand-rolled C# factory (not the JSON CardDefinition path)
///
/// The data-driven
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> only
/// supports the effect/ability shapes enumerated in its dispatch (counters,
/// draw, scry/surveil, stub damage, …). It has NO equip ability and NO
/// attached keyword-grant — a JSON def alone produces only a vanilla Artifact
/// shell (the shipped <c>fireshrieker.json</c> mirrors
/// <c>sword-of-vengeance.json</c> / <c>lavaspur-boots.json</c>, which carry
/// zero abilities). Every functioning keyword-grant equipment analogue
/// (<see cref="SwordOfVengeanceFactory"/>, <see cref="ShadowspearFactory"/>)
/// is hand-rolled for exactly this reason, so Fireshrieker follows that
/// established pattern.
///
/// ## Implementation
///
/// - <b>"Equipped creature has double strike"</b> (CR 702.4) — a single
///   Layer-6 <see cref="GrantAbilityEffect"/> (CR 613.1f, ability-adding)
///   that re-projects a fresh <see cref="KeywordAbility"/>("Double strike")
///   onto the live equipped creature. The selector reads
///   <see cref="Permanent.AttachedTo"/> at sync time, so re-equipping
///   transfers the grant and LTB / detach revoke it via the service's grant
///   lifecycle. <see cref="Majik.Core.Combat.CombatAbilities.HasDoubleStrike"/>
///   reads the granted marker through the computed keyword set; the canonical
///   keyword string "Double strike" matches that probe exactly. No P/T
///   modification (Fireshrieker prints no power/toughness boost).
/// - <b>Equip {2}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive, threading the
///   Puresteel-Paladin zero-equip cost-provider hook for cycle parity.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only (factory-shape / dispatch
/// tests). The keyword grant is not registered against any
/// <see cref="ContinuousEffectsService"/> on that path. Use the two-arg
/// overload to wire the continuous effect.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically (same gap as the rest of the
///   equipment cycle).
/// </summary>
[CardName("Fireshrieker")]
public static class FireshriekerFactory
{
    public const string CardName = "Fireshrieker";
    public const string Cost = "{3}";
    public const string EquipCost = "{2}";

    /// <summary>Granted keyword — CR 702.4 Double strike.</summary>
    public const string GrantedDoubleStrike = "Double strike";

    /// <summary>
    /// Constructs Fireshrieker with no live continuous-effects wiring (the
    /// shape / dispatcher path). The Double strike grant is NOT registered
    /// against any service.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs Fireshrieker. When <paramref name="continuousEffects"/> is
    /// supplied the "equipped creature has double strike" grant (Layer 6) is
    /// registered against it; it gates on Fireshrieker being on the
    /// battlefield AND attached to a battlefield permanent (the
    /// <see cref="GrantAbilityEffect"/> selector reads <c>card.AttachedTo</c>
    /// at sync time). When null, it is skipped.
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
        // "Equipped creature has double strike." (CR 702.4)
        // A single Layer-6 GrantAbilityEffect (CR 613.1f) re-projecting a
        // fresh KeywordAbility("Double strike") onto the live equipped
        // creature. The selector reads card.AttachedTo so re-equipping
        // transfers the grant and detach revokes it. Mirrors the keyword
        // grants in SwordOfVengeanceFactory.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: bearer =>
                    new KeywordAbility(
                        GrantedDoubleStrike, bearer, bearer.Controller ?? owner)));
        }

        // --------------------------------------------------------------
        // Equip {2} — standard equipment-cycle Equip activated ability
        // (CR 702.6) via the shared primitive. Threads the Puresteel
        // zero-cost provider hook for cycle parity.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
