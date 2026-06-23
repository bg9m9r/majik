using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for No-Dachi (Champions of Kamigawa, {2}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-06-23):
///   "Equipped creature gets +2/+0 and has first strike."
///   "Equip {3}"
///
/// ## Why a hand-rolled C# factory (not the bare JSON path)
///
/// The data-driven
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> has NO
/// equip ability, NO attached P/T boost, and NO attached keyword-grant, so a
/// JSON def alone produces only a vanilla Artifact/Equipment shell. The base
/// shape (name, Artifact, Equipment, {2}) is materialised from
/// <c>no-dachi.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the equipment behaviour is
/// layered on here, mirroring the rest of the equip cycle
/// (<see cref="ONaginataFactory"/>, <see cref="BonesplitterFactory"/>,
/// <see cref="SwiftfootBootsFactory"/>).
///
/// ## Implementation
///
/// - <b>"Equipped creature gets +2/+0 and has first strike."</b> — two
///   <see cref="AttachedBoostEffect"/>s: a +2/+0 P/T modification at Layer 7c
///   (CR 613 Layer 7c) and a parallel "First Strike" keyword grant at
///   <see cref="Layer.Abilities"/> (CR 613.1c — Layer 6 ability addition).
///   Same paired shape as <see cref="ONaginataFactory"/>'s "+3/+0 and has
///   trample". <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/>
///   reads the "First Strike" marker off the equipped creature's computed
///   keyword set (the keyword set is an OrdinalIgnoreCase HashSet, so the
///   grant matches the combat lookup's "First strike" query regardless of
///   casing). Each effect gates on No-Dachi being on the battlefield AND
///   attached (<see cref="AttachedBoostEffect.IsActive"/>), so re-equipping
///   transfers the boost and detach / LTB revokes it.
/// - <b>Equip {3}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive with the Puresteel zero-
///   equip cost-provider hook for cycle parity.
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied, the +2/+0 boost
/// (Layer 7c) and the First Strike grant (Layer 6) are registered immediately;
/// each gates on No-Dachi being on the battlefield AND attached. The
/// single-arg <see cref="Create(Player)"/> overload omits service wiring and
/// produces the correct card shape only (factory-shape / dispatch tests).
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for the Equip activation — v1 picks the
///   first controller-side creature deterministically (same gap as the rest
///   of the equipment cycle).
/// </summary>
[CardName("No-Dachi")]
public static class NoDachiFactory
{
    public const string CardName = "No-Dachi";
    public const string Slug = "no-dachi";
    public const string EquipCost = "{3}";

    /// <summary>The granted keyword — canonical string matching the combat
    /// lookup in <see cref="Majik.Core.Combat.CombatAbilities"/>. The computed
    /// keyword set is OrdinalIgnoreCase, so casing is immaterial.</summary>
    public const string FirstStrike = "First Strike";

    /// <summary>
    /// Constructs No-Dachi with no live continuous-effects wiring (the
    /// shape / dispatcher path). The +2/+0 boost and First Strike grant are NOT
    /// registered against any service. Suitable for factory-shape / dispatch
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs No-Dachi. When <paramref name="continuousEffects"/> is
    /// supplied, the +2/+0 boost (Layer 7c) and the First Strike grant (Layer 6)
    /// are registered against it; both gate on No-Dachi being on the
    /// battlefield AND attached to a battlefield permanent. When null, the
    /// grants are skipped.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact,
        // Equipment, {2}). No abilities in the JSON — the equipment behaviour
        // is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        // --------------------------------------------------------------
        // "Equipped creature gets +2/+0 and has first strike." Two
        // AttachedBoostEffects: Layer 7c for the +2/+0, Layer 6 for the
        // granted First Strike keyword (CR 613.7c + CR 613.1c). Same paired-
        // effect shape as O-Naginata's "+3/+0 and has trample". Each gates on
        // the source being on the battlefield AND attached
        // (AttachedBoostEffect.IsActive).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 2, toughness: 0));

            continuousEffects.Register(
                new AttachedBoostEffect(
                    source: card,
                    power: 0,
                    toughness: 0,
                    grantedKeywords: new[] { FirstStrike },
                    layer: Layer.Abilities));
        }

        // --------------------------------------------------------------
        // Equip {3} — activated ability (CR 702.6) via the shared primitive.
        // Threads the Puresteel zero-cost provider hook for cycle parity.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
