using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bone Saw (Mirrodin, {0}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +1/+0."
///   "Equip {2}."
///
/// Cheap Affinity / artifact-count enabler — a 0-mana artifact whose
/// activated cost is too steep to actually equip in Modern, but its
/// presence on the battlefield is the printable form of Lotus Bloom /
/// Mox Opal-tier artifact-density (Cranial Plating's +1/+0 per artifact,
/// Mox Opal's Metalcraft gate, Affinity discount, etc.). Hammer Time and
/// Hardened Scales decks treat it as a free permanent.
///
/// ## Implementation
///
/// - <b>Static "equipped creature gets +1/+0"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (P/T modification, CR
///   613 Layer 7c). The effect reads the source's
///   <see cref="Permanent.AttachedTo"/> dynamically, so re-equipping
///   transfers the boost without re-registration. Identical lifecycle
///   shape to <see cref="SkullclampFactory"/> / <see cref="ColossusHammerFactory"/>
///   — gated on the Bone Saw being on the battlefield AND attached.
/// - <b>Equip {2}</b> — activated ability (CR 702.6a / 702.6d) wired via
///   the <see cref="EquipActivatedAbility"/> primitive (PR #471).
///   Sorcery-speed gate, "creature you control" target gathering, attach
///   resolution, and the Puresteel Paladin zero-equip cost-provider hook
///   are all encapsulated. Same shape as Cranial Plating / Skullclamp /
///   Colossus Hammer.
/// - Mana cost is the literal {0} string (same convention as
///   <see cref="MemniteFactory"/> / <see cref="MoxOpalFactory"/>).
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied, the +1/+0 boost
/// is registered immediately; its <c>IsActive</c> gates on Bone Saw being
/// on the battlefield AND attached to a battlefield permanent, so an
/// unequipped (or off-battlefield) Bone Saw silently contributes nothing.
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
[CardName("Bone Saw")]
public static class BoneSawFactory
{
    public const string CardName = "Bone Saw";
    public const string PrintedManaCost = "{0}";
    public const string EquipCost = "{2}";

    /// <summary>
    /// Constructs a Bone Saw with no live continuous-effects wiring
    /// (the shape / dispatcher path). The Equip activated ability is
    /// attached but the +1/+0 boost is not registered against any
    /// <see cref="ContinuousEffectsService"/>.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs a Bone Saw. When <paramref name="continuousEffects"/>
    /// is supplied, the static +1/+0 boost (Layer 7c) is registered
    /// against it; the effect is gated on Bone Saw being on the
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
        // Static continuous effect — "Equipped creature gets +1/+0."
        // Gates on the source being on the battlefield AND attached
        // (see AttachedBoostEffect.IsActive). CR 613 Layer 7c.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 1, toughness: 0));
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
