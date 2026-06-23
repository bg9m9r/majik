using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Darksteel Axe (Aether Revolt, {1}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-06-23):
///   "Indestructible (Effects that say \"destroy\" don't destroy this
///    Equipment.)"
///   "Equipped creature gets +2/+0."
///   "Equip {2}"
///
/// Mechanically the indestructible cousin of <see cref="BonesplitterFactory"/>
/// — same flat +2/+0 buff, but Equip {2} (vs Bonesplitter's {1}) and the
/// intrinsic Indestructible keyword (CR 702.12) that protects the Equipment
/// itself from "destroy" effects. Built on the same JSON shell + C# wiring
/// split used across the equipment cycle (the JSON expresses only the
/// artifact / Equipment shape; the buff, equip ability, and keyword marker
/// are wired here).
///
/// ## Implementation
///
/// - <b>Indestructible</b> (CR 702.12) — wired as a
///   <see cref="KeywordAbility"/> marker on the Equipment. The non-creature
///   destroy gate in
///   <see cref="Majik.Core.CardData.OracleSpellBinder.HasIndestructible"/>
///   reads this marker, so "destroy" effects (e.g. Abrade, Disenchant) are
///   cancelled against the Axe itself. Mirrors
///   <see cref="DarksteelCitadelFactory"/>'s printed-Indestructible wiring.
/// - <b>"Equipped creature gets +2/+0"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (P/T modification, CR 613
///   Layer 7c). Reads <see cref="Permanent.AttachedTo"/> dynamically so
///   re-equipping transfers the boost; gated on the Axe being on the
///   battlefield AND attached.
/// - <b>Equip {2}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive (sorcery-speed gate,
///   "creature you control" target gathering, attach resolution, and the
///   Puresteel-Paladin zero-equip cost-provider hook all encapsulated).
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits service wiring
/// (factory-shape / dispatch tests). The two-arg overload registers the
/// +2/+0 boost; its <c>IsActive</c> gates on the Axe being on the battlefield
/// AND attached, so an unequipped (or off-battlefield) Axe contributes
/// nothing.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for "creature you control" (CR 702.6b)
///   — v1 picks the first controller-side creature deterministically
///   (inherited from <see cref="EquipActivatedAbility"/>).
/// </summary>
[CardName("Darksteel Axe")]
public static class DarksteelAxeFactory
{
    public const string CardName = "Darksteel Axe";
    public const string PrintedManaCost = "{1}";
    public const string EquipCost = "{2}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("darksteel-axe");

    /// <summary>
    /// Constructs Darksteel Axe with no live continuous-effects wiring (the
    /// shape / dispatcher path). The +2/+0 boost is not registered against any
    /// <see cref="ContinuousEffectsService"/>.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs Darksteel Axe. When <paramref name="continuousEffects"/> is
    /// supplied, the static +2/+0 boost (Layer 7c) is registered against it;
    /// the effect gates on the Axe being on the battlefield and attached to a
    /// battlefield permanent.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // CardDefinitionFactory.Build already owner/controller-sets the card.
        var card = (Artifact)CardDefinitionFactory.Build(Definition, owner);

        // --------------------------------------------------------------
        // Indestructible (CR 702.12). Marker only — the non-creature
        // destroy gate (OracleSpellBinder.HasIndestructible) reads this
        // KeywordAbility off the Permanent and cancels "destroy" effects.
        // --------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Indestructible", card, owner));

        // --------------------------------------------------------------
        // Static continuous effect — "Equipped creature gets +2/+0."
        // Gates on the source being on the battlefield AND attached
        // (see AttachedBoostEffect.IsActive). CR 613 Layer 7c.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 2, toughness: 0));
        }

        // --------------------------------------------------------------
        // Equip {2} — activated ability (CR 702.6) via the
        // EquipActivatedAbility primitive. Sorcery-speed gate, target-
        // gathering, attach resolution, and Puresteel zero-equip
        // cost-provider hook are all encapsulated.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
