using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nemesis Mask (Mirrodin Besieged, {3}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-06-02):
///   "All creatures able to block equipped creature do so."
///   "Equip {3}"
///
/// The equipment analogue of Lure — strap it on and every untapped able
/// creature the defending player controls must gang-block the equipped
/// creature (CR 509.1c / 509.1g).
///
/// ## Why a hand-rolled C# factory (not the JSON CardDefinition path)
///
/// The data-driven
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> only
/// supports the effect/ability shapes enumerated in its dispatch (counters,
/// draw, scry/surveil, stub damage, …). It has NO equip ability and NO
/// attached keyword-grant — a JSON def alone produces only a vanilla Artifact
/// shell. The functioning equipment analogues
/// (<see cref="LightningGreavesFactory"/>, <see cref="LavaspurBootsFactory"/>)
/// are hand-rolled for exactly this reason, so Nemesis Mask follows that
/// established pattern.
///
/// ## Implementation — the aura/equipment keyword-grant rail
///
/// - <b>"All creatures able to block equipped creature do so"</b> — granted
///   to the equipped host as the <c>"MustBeBlockedByAllAble"</c> marker
///   keyword via a Layer-6 <see cref="GrantAbilityEffect"/> (CR 613.1f)
///   re-projecting a fresh <see cref="KeywordAbility"/> onto the live equipped
///   creature. The selector reads <see cref="Permanent.AttachedTo"/> at sync
///   time, so re-equipping transfers the grant; LTB / detach revoke it via the
///   service's grant lifecycle (CR 613.6e). The granted marker is the SAME
///   keyword Breaker of Armies carries printed, so
///   <see cref="Majik.Core.Combat.CombatAbilities.MustBeBlockedByAllAble"/>
///   reads it through the computed keyword set and the must-block overload of
///   <c>CombatValidator.IsValidBlockDeclaration</c> enforces it (CR 509.1c).
/// - <b>Equip {3}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive, threading the
///   Puresteel-Paladin zero-equip cost-provider hook for cycle parity.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only (factory-shape / dispatch
/// tests). Use the two-arg overload to wire the keyword grant; it gates on the
/// Mask being on the battlefield AND attached to a battlefield permanent.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically (same gap as the rest of the
///   equipment cycle).
/// </summary>
[CardName("Nemesis Mask")]
public static class NemesisMaskFactory
{
    public const string CardName = "Nemesis Mask";
    public const string Cost = "{3}";

    /// <summary>CR 702.6 — printed equip cost: {3}.</summary>
    public const string EquipCost = "{3}";

    /// <summary>The marker keyword granted to the equipped creature — the same
    /// one Breaker of Armies carries printed (CR 509.1c).</summary>
    public const string GrantedKeyword = "MustBeBlockedByAllAble";

    /// <summary>
    /// Constructs Nemesis Mask with no live continuous-effects wiring (the
    /// shape / dispatcher path). The keyword grant is not registered against
    /// any service.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs Nemesis Mask. When <paramref name="continuousEffects"/> is
    /// supplied the "all creatures able to block equipped creature do so"
    /// keyword grant (CR 509.1c, Layer 6) is registered against it; it gates on
    /// the Mask being on the battlefield AND attached to a battlefield
    /// permanent. When null, it is skipped.
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
        // "All creatures able to block equipped creature do so."
        // Gates on the source being on the battlefield AND attached
        // (GrantAbilityEffect selector reads AttachedTo). CR 509.1c,
        // CR 613.1f, Layer 6.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: bearer =>
                    new KeywordAbility(GrantedKeyword, bearer, bearer.Controller ?? owner)));
        }

        // --------------------------------------------------------------
        // Equip {3} — standard equipment-cycle Equip activated ability
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
