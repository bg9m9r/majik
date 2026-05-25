using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bonesplitter (Mirrodin, {1}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +3/+0."
///   "Equip {1}."
///
/// ## Implementation
///
/// - <b>Static "equipped creature gets +3/+0"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (P/T modification, CR
///   613 Layer 7c). The effect reads the source's
///   <see cref="Permanent.AttachedTo"/> dynamically, so re-equipping
///   transfers the boost without re-registration. Mirrors
///   <see cref="ColossusHammerFactory"/>'s shape (sans the lose-flying
///   rider) — Bonesplitter is the simplest possible Equipment after Bone
///   Saw, just a flat +3/+0 with no secondary effect.
/// - <b>Equip {1}</b> — activated ability (CR 702.6a / 702.6d). Cost is
///   <c>{1}</c>. Target is "a creature you control" (CR 702.6b). v1 picker
///   is deterministic (delegated to <see cref="EquipActivatedAbility"/>'s
///   first-creature-on-controller-battlefield fallback) — real targeting
///   prompt deferred. Sorcery-speed restriction (CR 702.6a) is enforced
///   by the primitive.
/// - <b>Puresteel Paladin hook</b>: equip cost is provided through
///   <see cref="PuresteelPaladinFactory.ZeroEquipCostProvider"/>, matching
///   the shape every other Equipment uses so that "Equipment you control
///   have equip {0}" actually reduces this card's printed equip cost.
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied the +3/+0 boost
/// is registered immediately. The effect's <c>IsActive</c> gates on
/// Bonesplitter being on the battlefield AND attached to a battlefield
/// permanent — silently contributing nothing while unequipped or in hand
/// / graveyard / etc.
///
/// The single-arg <see cref="Create(Player)"/> overload omits service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for "creature you control" (CR 702.6b)
///   — v1 picks the first controller-side creature deterministically
///   (same gap as <see cref="ColossusHammerFactory"/> /
///   <see cref="SwordOfFireAndIceFactory"/>).
/// </summary>
[CardName("Bonesplitter")]
public static class BonesplitterFactory
{
    public const string CardName = "Bonesplitter";
    public const string Cost = "{1}";
    public const string EquipCost = "{1}";

    /// <summary>
    /// Constructs a Bonesplitter with no live continuous-effects wiring
    /// (the shape / dispatcher path). The Equip activated ability is
    /// attached but the P/T boost is not registered against any service.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs a Bonesplitter. When <paramref name="continuousEffects"/>
    /// is supplied, the static +3/+0 boost (Layer 7c) is registered against
    /// it; the effect is gated on Bonesplitter being on the battlefield
    /// and attached to a battlefield permanent. When null, the effect is
    /// skipped.
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
        // Static continuous effect — "Equipped creature gets +3/+0."
        // CR 613 Layer 7c. AttachedBoostEffect.IsActive gates on the
        // source being on the battlefield AND attached.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 3, toughness: 0));
        }

        // --------------------------------------------------------------
        // Equip {1} — activated ability (CR 702.6) via the
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
