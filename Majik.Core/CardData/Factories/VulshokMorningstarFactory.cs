using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vulshok Morningstar (Fifth Dawn / Mirrodin, {2}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-06-23):
///   "Equipped creature gets +2/+2."
///   "Equip {2}"
///
/// A flat +X/+X Equipment with a generic equip cost — mechanically identical
/// in shape to <see cref="BonesplitterFactory"/> (+2/+0, Equip {1}), differing
/// only in the boost magnitude (+2/+2 vs +2/+0) and the equip cost ({2} vs
/// {1}). Shares the +2/+2 boost magnitude with
/// <see cref="MaulOfTheSkyclavesFactory"/> but carries none of the Maul's
/// keyword grants or ETB attach trigger.
///
/// ## Why a hand-rolled C# factory (not the JSON CardDefinition path)
///
/// The data-driven
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> has NO
/// equip ability and NO dynamic attached-boost effect, so a JSON def alone
/// produces only a vanilla Artifact shell. The shipped
/// <c>vulshok-morningstar.json</c> mirrors <c>maul-of-the-skyclaves.json</c> /
/// <c>lavaspur-boots.json</c>: name + types + subtypes + cost only. The
/// functioning behaviour is hand-rolled here, the established pattern across
/// the equipment cycle (<see cref="BonesplitterFactory"/>,
/// <see cref="MaulOfTheSkyclavesFactory"/>).
///
/// ## Implementation
///
/// - <b>Static "equipped creature gets +2/+2"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (P/T modification, CR 613
///   Layer 7c). The effect reads the source's
///   <see cref="Permanent.AttachedTo"/> dynamically, so re-equipping transfers
///   the boost without re-registration. Gated on the Morningstar being on the
///   battlefield AND attached.
/// - <b>Equip {2}</b> — activated ability (CR 702.6a / 702.6d) wired via the
///   <see cref="EquipActivatedAbility"/> primitive. Sorcery-speed gate (CR
///   117.1a / 307.5), "creature you control" target gathering (CR 702.6b),
///   attach resolution, and the Puresteel Paladin zero-equip cost-provider
///   hook are all encapsulated.
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied, the +2/+2 boost is
/// registered immediately; its <c>IsActive</c> gates on Vulshok Morningstar
/// being on the battlefield AND attached to a battlefield permanent, so an
/// unequipped (or off-battlefield) Morningstar silently contributes nothing.
///
/// The single-arg <see cref="Create(Player)"/> overload omits service wiring
/// and produces the correct card shape only — suitable for factory-shape /
/// dispatch tests.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for "creature you control" (CR 702.6b) — v1
///   picks the first controller-side creature deterministically (inherited
///   from <see cref="EquipActivatedAbility"/>).
/// </summary>
[CardName("Vulshok Morningstar")]
public static class VulshokMorningstarFactory
{
    public const string CardName = "Vulshok Morningstar";
    public const string PrintedManaCost = "{2}";
    public const string EquipCost = "{2}";

    /// <summary>
    /// Constructs a Vulshok Morningstar with no live continuous-effects wiring
    /// (the shape / dispatcher path). The Equip activated ability is attached
    /// but the +2/+2 boost is not registered against any
    /// <see cref="ContinuousEffectsService"/>.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs a Vulshok Morningstar. When
    /// <paramref name="continuousEffects"/> is supplied, the static +2/+2 boost
    /// (Layer 7c) is registered against it; the effect is gated on the
    /// Morningstar being on the battlefield and attached to a battlefield
    /// permanent.
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
        // Static continuous effect — "Equipped creature gets +2/+2."
        // Gates on the source being on the battlefield AND attached
        // (see AttachedBoostEffect.IsActive). CR 613 Layer 7c.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 2, toughness: 2));
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
