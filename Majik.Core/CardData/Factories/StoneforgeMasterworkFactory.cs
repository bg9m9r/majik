using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stoneforge Masterwork (Aether Revolt, {1}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +1/+1 for each other creature you control
///    that shares a creature type with it."
///   "Equip {2}."
///
/// ## Implementation
///
/// - <b>Static "+N/+N" where N = count of other creatures you control
///   sharing at least one creature subtype with the equipped creature</b>
///   — registered via the dynamic-N <see cref="AttachedBoostEffect"/>
///   overload (Layer 7c, CR 613 Layer 7c). The closure samples the
///   live equipped creature (`card.AttachedTo`) AND the controller's
///   battlefield on every layer pass:
///     - 0 when no creature is equipped (gated by `IsActive` already).
///     - Otherwise enumerate `controller.Zones.Battlefield.GetCards()`
///       filtered to `Creature` excluding the equipped creature itself
///       (printed "other") and intersect `Subtypes` with the equipped
///       creature's subtypes restricted to creature subtypes (see the
///       inline `IsCreatureSubtype` predicate; mirrors the carve-out
///       used by <see cref="OkoThiefOfCrownsFactory"/>).
///   Mirrors <see cref="CranialPlatingFactory"/>'s dynamic-N posture but
///   with both power AND toughness sampling the same closure (printed
///   "+N/+N" not "+N/+0").
/// - <b>Equip {2}</b> — activated ability (CR 702.6) via the
///   <see cref="EquipActivatedAbility"/> primitive. Threads the
///   Puresteel-Paladin zero-equip cost-provider hook for consistency
///   with the rest of the equipment cycle.
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied, the dynamic
/// +N/+N boost is registered immediately; its <c>IsActive</c> gates on
/// the Masterwork being on the battlefield AND attached to a battlefield
/// permanent. A Masterwork that has not been equipped (or that has left
/// the battlefield) silently contributes nothing.
///
/// The single-arg <see cref="Create(Player)"/> overload omits service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically (same gap as the rest of
///   the equipment cycle).
/// - <b>Type-changing effects on the equipped creature</b> — the
///   subtype-intersection closure reads `Card.Subtypes` directly rather
///   than the Layer 4 working type-line. Conspiracy / Arcane Adaptation
///   would currently miscount; same gap as Scion of Draco's
///   per-creature-type predicate.
/// </summary>
[CardName("Stoneforge Masterwork")]
public static class StoneforgeMasterworkFactory
{
    public const string CardName = "Stoneforge Masterwork";
    public const string Cost = "{1}";
    public const string EquipCost = "{2}";

    /// <summary>
    /// Constructs Stoneforge Masterwork with no live continuous-effects
    /// wiring (the shape / dispatcher path). Boost not registered against
    /// any service; the Equip ability is attached to the card.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs Stoneforge Masterwork. When
    /// <paramref name="continuousEffects"/> is supplied, the dynamic
    /// +N/+N boost (Layer 7c) is registered against it; the effect gates
    /// on the Masterwork being on the battlefield and attached to a
    /// battlefield permanent. When null, the boost is skipped.
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
        // Static "Equipped creature gets +1/+1 for each other creature
        // you control that shares a creature type with it."
        //
        // Dynamic-N AttachedBoostEffect — both power AND toughness
        // evaluators return the same shared-subtype count, sampled on
        // every layer pass (CR 613 Layer 7c).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                powerFn: () => CountSharedSubtypeCreatures(card),
                toughnessFn: () => CountSharedSubtypeCreatures(card)));
        }

        // --------------------------------------------------------------
        // Equip {2} — standard equipment-cycle Equip activated ability
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

    /// <summary>
    /// Live count of OTHER creatures the Masterwork's current controller
    /// controls that share at least one creature subtype with the
    /// currently-equipped creature. Reads the controller and the
    /// equipped creature dynamically (not at factory-construction time)
    /// so control-change effects and re-equipping retarget the count
    /// correctly. Returns 0 when no creature is equipped or when the
    /// Masterwork has no live controller (off-battlefield / orphaned) so
    /// the boost gates cleanly via <see cref="AttachedBoostEffect.IsActive"/>.
    /// </summary>
    public static int CountSharedSubtypeCreatures(Permanent masterwork)
    {
        var ctrl = masterwork.Controller ?? masterwork.Owner;
        if (ctrl == null) return 0;

        var equipped = masterwork.AttachedTo;
        if (equipped == null) return 0;

        var equippedCreatureSubtypes = equipped.Subtypes
            .Where(IsCreatureSubtype)
            .ToHashSet();
        if (equippedCreatureSubtypes.Count == 0) return 0;

        return ctrl.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c =>
                !ReferenceEquals(c, equipped)
                && c.Subtypes.Any(equippedCreatureSubtypes.Contains));
    }

    /// <summary>
    /// Predicate identifying creature-subtype enum members. The
    /// <see cref="CardSubtype"/> enum carries no category metadata, so
    /// the well-known non-creature subtype ranges (Land / Artifact /
    /// Enchantment / Planeswalker) are explicitly excluded — same
    /// carve-out shape as <see cref="OkoThiefOfCrownsFactory"/>.
    /// Conservative: any unknown / future value is treated as a creature
    /// subtype (matches printed "shares a creature type with it" which
    /// only references creature subtypes).
    /// </summary>
    private static bool IsCreatureSubtype(CardSubtype st) => st switch
    {
        CardSubtype.Forest or CardSubtype.Island or CardSubtype.Mountain
            or CardSubtype.Plains or CardSubtype.Swamp or CardSubtype.Wastes
            or CardSubtype.Desert or CardSubtype.Gate or CardSubtype.Lair
            or CardSubtype.Locus or CardSubtype.Mine or CardSubtype.PowerPlant
            or CardSubtype.Tower or CardSubtype.Urzas => false,
        CardSubtype.Aura or CardSubtype.Saga or CardSubtype.Shrine => false,
        CardSubtype.Equipment or CardSubtype.Vehicle or CardSubtype.Food
            or CardSubtype.Treasure or CardSubtype.Clue
            or CardSubtype.Construct or CardSubtype.Blood
            or CardSubtype.Powerstone => false,
        CardSubtype.Ajani or CardSubtype.Ashiok or CardSubtype.Chandra
            or CardSubtype.Grist or CardSubtype.Jace or CardSubtype.Liliana
            or CardSubtype.Garruk or CardSubtype.Nissa or CardSubtype.Teferi
            or CardSubtype.Karn or CardSubtype.Ugin or CardSubtype.Bolas
            or CardSubtype.Wrenn or CardSubtype.Oko => false,
        _ => true,
    };
}
