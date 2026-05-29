using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bonesplitter (Mirrodin, {1}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +2/+0."
///   "Equip {1}."
///
/// A premium aggro Equipment — cheap to cast and cheap to equip, the
/// classic "+2 power for {1}" enabler in mono-white/red beatdown shells.
/// Mechanically a flat +X/+0 equip identical in shape to
/// <see cref="BoneSawFactory"/> (+1/+0, Equip {2}) and
/// <see cref="ColossusHammerFactory"/> (+10/+0), differing only in the
/// boost magnitude (+2/+0) and the equip cost ({1}).
///
/// ## Implementation
///
/// - <b>Static "equipped creature gets +2/+0"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (P/T modification, CR
///   613 Layer 7c). The effect reads the source's
///   <see cref="Permanent.AttachedTo"/> dynamically, so re-equipping
///   transfers the boost without re-registration. Gated on the
///   Bonesplitter being on the battlefield AND attached.
/// - <b>Equip {1}</b> — activated ability (CR 702.6a / 702.6d) wired via
///   the <see cref="EquipActivatedAbility"/> primitive. Sorcery-speed gate
///   (CR 117.1a / 307.5), "creature you control" target gathering (CR
///   702.6b), attach resolution, and the Puresteel Paladin zero-equip
///   cost-provider hook are all encapsulated.
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied, the +2/+0 boost
/// is registered immediately; its <c>IsActive</c> gates on Bonesplitter
/// being on the battlefield AND attached to a battlefield permanent, so an
/// unequipped (or off-battlefield) Bonesplitter silently contributes
/// nothing.
///
/// The single-arg <see cref="Create(Player)"/> overload omits service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for "creature you control" (CR 702.6b)
///   — v1 picks the first controller-side creature deterministically
///   (inherited from <see cref="EquipActivatedAbility"/>).
/// </summary>
[CardName("Bonesplitter")]
public static class BonesplitterFactory
{
    public const string CardName = "Bonesplitter";
    public const string PrintedManaCost = "{1}";
    public const string EquipCost = "{1}";

    /// <summary>
    /// Constructs a Bonesplitter with no live continuous-effects wiring
    /// (the shape / dispatcher path). The Equip activated ability is
    /// attached but the +2/+0 boost is not registered against any
    /// <see cref="ContinuousEffectsService"/>.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs a Bonesplitter. When <paramref name="continuousEffects"/>
    /// is supplied, the static +2/+0 boost (Layer 7c) is registered
    /// against it; the effect is gated on Bonesplitter being on the
    /// battlefield and attached to a battlefield permanent.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: PrintedManaCost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

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
