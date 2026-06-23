using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for O-Naginata (Champions of Kamigawa, {1}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-06-23):
///   "This Equipment can be attached only to a creature with power 3 or
///    greater."
///   "Equipped creature gets +3/+0 and has trample."
///   "Equip {2}"
///
/// ## Why a hand-rolled C# factory (not the bare JSON path)
///
/// The data-driven
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> has NO
/// equip ability, NO attached P/T boost, NO attached keyword-grant, and NO
/// equip restriction, so a JSON def alone produces only a vanilla
/// Artifact/Equipment shell. The base shape (name, Artifact, Equipment, {1})
/// is materialised from <c>o-naginata.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the equipment behaviour is
/// layered on here, mirroring the rest of the equip cycle
/// (<see cref="ShadowspearFactory"/>, <see cref="BonesplitterFactory"/>).
///
/// ## Implementation
///
/// - <b>"Equipped creature gets +3/+0 and has trample."</b> — two
///   <see cref="AttachedBoostEffect"/>s: a +3/+0 P/T modification at Layer 7c
///   (CR 613 Layer 7c) and a parallel "Trample" keyword grant at
///   <see cref="Layer.Abilities"/> (CR 613.1c — Layer 6 ability addition).
///   Same paired shape as Shadowspear's "+1/+1 and has trample and lifelink".
///   <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/> reads the
///   "Trample" marker off the equipped creature's computed keyword set. Each
///   effect gates on O-Naginata being on the battlefield AND attached
///   (<see cref="AttachedBoostEffect.IsActive"/>), so re-equipping transfers
///   the boost and detach / LTB revokes it.
/// - <b>"Can be attached only to a creature with power 3 or greater."
///   (CR 702.6e)</b> — an attach restriction predicate
///   (<c>c =&gt; c.GetPower() &gt;= 3</c>) threaded into the shared
///   <see cref="EquipActivatedAbility"/>. It narrows the legal attach targets
///   in the candidate gatherer and is re-checked at resolution
///   (CR 608.2b) so a creature whose power drops below 3 before the equip
///   resolves is no longer a legal bearer.
/// - <b>Equip {2}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive with the Puresteel zero-
///   equip cost-provider hook for cycle parity.
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied, the +3/+0 boost
/// (Layer 7c) and the Trample grant (Layer 6) are registered immediately;
/// each gates on O-Naginata being on the battlefield AND attached. The
/// single-arg <see cref="Create(Player)"/> overload omits service wiring and
/// produces the correct card shape only (factory-shape / dispatch tests).
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for the Equip activation — v1 picks the
///   first power-3+ controller-side creature deterministically (same gap as
///   the rest of the equipment cycle).
/// </summary>
[CardName("O-Naginata")]
public static class ONaginataFactory
{
    public const string CardName = "O-Naginata";
    public const string Slug = "o-naginata";
    public const string EquipCost = "{2}";

    /// <summary>The granted keyword — canonical string matching the combat
    /// lookup in <see cref="Majik.Core.Combat.CombatAbilities"/>.</summary>
    public const string Trample = "Trample";

    /// <summary>
    /// CR 702.6e — O-Naginata can be attached only to a creature with power 3
    /// or greater. Reads the live (effect-modified) power via
    /// <see cref="Creature.GetPower"/>.
    /// </summary>
    public static bool MeetsAttachRestriction(Creature c)
        => c is not null && c.GetPower() >= 3;

    /// <summary>
    /// Constructs O-Naginata with no live continuous-effects wiring (the
    /// shape / dispatcher path). The +3/+0 boost and Trample grant are NOT
    /// registered against any service. Suitable for factory-shape / dispatch
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs O-Naginata. When <paramref name="continuousEffects"/> is
    /// supplied, the +3/+0 boost (Layer 7c) and the Trample grant (Layer 6)
    /// are registered against it; both gate on O-Naginata being on the
    /// battlefield AND attached to a battlefield permanent. When null, the
    /// grants are skipped.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact,
        // Equipment, {1}). No abilities in the JSON — the equipment behaviour
        // is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        // --------------------------------------------------------------
        // "Equipped creature gets +3/+0 and has trample." Two
        // AttachedBoostEffects: Layer 7c for the +3/+0, Layer 6 for the
        // granted Trample keyword (CR 613.7c + CR 613.1c). Same paired-effect
        // shape as Shadowspear's "+1/+1 and has trample and lifelink". Each
        // gates on the source being on the battlefield AND attached
        // (AttachedBoostEffect.IsActive).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 3, toughness: 0));

            continuousEffects.Register(
                new AttachedBoostEffect(
                    source: card,
                    power: 0,
                    toughness: 0,
                    grantedKeywords: new[] { Trample },
                    layer: Layer.Abilities));
        }

        // --------------------------------------------------------------
        // Equip {2} — activated ability (CR 702.6) via the shared primitive,
        // narrowed by the CR 702.6e equip restriction (power 3 or greater).
        // Threads the Puresteel zero-cost provider hook for cycle parity.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider,
            attachRestriction: MeetsAttachRestriction);

        card.AddAbility(equipAbility);

        return card;
    }
}
