using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Colossus Hammer (Modern Horizons, {1}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +10/+0 and loses flying."
///   "Equip {8}."
///
/// ## Implementation
///
/// - <b>Static "equipped creature gets +10/+0"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (P/T modification, CR
///   613 Layer 7c). The effect reads the source's
///   <see cref="Permanent.AttachedTo"/> dynamically, so re-equipping
///   transfers the boost without re-registration.
/// - <b>Static "loses flying"</b> — registered via
///   <see cref="LoseKeywordEffect"/>("Flying") at Layer 6
///   (CR 613.1d / 613.6 ability-removing). Narrow single-keyword removal
///   scoped to the bearer; printed Flying on the equipped creature stays
///   on the underlying card but is stripped from the in-flight
///   <see cref="CreatureCharacteristics.Keywords"/> working set.
/// - <b>Equip {8}</b> — activated ability (CR 702.6a / 702.6d). Cost is
///   <c>{8}</c>. Target is "a creature you control" (CR 702.6b). v1
///   picker is deterministic: the first creature on the controller's
///   battlefield. Real targeting prompt deferred.
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied, both continuous
/// effects are registered immediately. Each effect's <c>IsActive</c> gates
/// on Colossus Hammer being on the battlefield AND attached to a
/// battlefield permanent, so a Colossus Hammer that has not been equipped
/// (or that has left the battlefield) silently contributes nothing. This
/// matches the gating used by <see cref="AttachedBoostEffect"/> for auras
/// and other equipment.
///
/// The single-arg <see cref="Create(Player)"/> overload omits service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for "creature you control" (CR 702.6b)
///   — v1 picks the first controller-side creature deterministically.
///
/// CR 117.1a / 307.5 sorcery-speed restriction is now enforced via the
/// ActionValidator gate (<c>sorcerySpeed: true</c> on the Equip
/// activation).
/// </summary>
[CardName("Colossus Hammer")]
public static class ColossusHammerFactory
{
    public const string CardName = "Colossus Hammer";
    public const string Cost = "{1}";
    public const string EquipCost = "{8}";

    /// <summary>
    /// Constructs a Colossus Hammer with no live continuous-effects wiring
    /// (the shape / dispatcher path). The Equip activated ability is
    /// attached but neither the P/T boost nor the lose-flying effect is
    /// registered against any service.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs a Colossus Hammer. When <paramref name="continuousEffects"/>
    /// is supplied, the static +10/+0 boost (Layer 7c) and lose-flying
    /// (Layer 6) effects are registered against it; each is gated on the
    /// Hammer being on the battlefield and attached to a battlefield
    /// permanent. When null, both effects are skipped.
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
        // Static continuous effects — "Equipped creature gets +10/+0
        // and loses flying." Both effects gate on the source being on
        // the battlefield AND attached (see effect IsActive checks).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            // CR 613 Layer 7c — P/T modification.
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 10, toughness: 0));

            // CR 613.6 — Layer 6 ability-removing, single-keyword scope.
            continuousEffects.Register(
                new LoseKeywordEffect(card, "Flying"));
        }

        // --------------------------------------------------------------
        // Equip {8} — activated ability (CR 702.6) via the
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
