using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.1d / 205.3 / 514 — Layer 4 (type-changing) continuous effect that
/// removes every <i>creature</i> subtype from a fixed target creature until
/// the cleanup step (CR 514.2). The subtype-slot sibling of
/// <see cref="LoseKeywordUntilEndOfTurnEffect"/> (Layer 6) and
/// <see cref="PumpUntilEndOfTurnEffect"/> (Layer 7c): where those strip a
/// keyword / shift P/T on a specific creature with end-of-turn expiry, this
/// one strips the creature-type subtypes.
///
/// Used by Nameless Inversion's "...and loses all creature types until end of
/// turn" rider — paired with a <see cref="PumpUntilEndOfTurnEffect"/>(+3, -3)
/// on the same target.
///
/// <para>
/// Only subtypes that are <i>creature</i> types are removed; land / artifact /
/// enchantment / planeswalker subtypes a creature might carry (e.g. an Equipment
/// or Vehicle that is also a creature) are preserved — same conservative
/// carve-out shape used by Stoneforge Masterwork's <c>IsCreatureSubtype</c>
/// predicate. CR 205.3m enumerates creature subtypes as a distinct category.
/// </para>
///
/// The strip runs at Layer 4, <i>after</i> the printed subtype seed (which, for
/// a Changeling creature, is the full creature-type set), so it correctly wipes
/// even a creature that "is every creature type". Any later-timestamp Layer 4
/// ADD-subtype effect re-adds per CR 613.7 timestamp ordering.
/// </summary>
public sealed class LoseAllCreatureTypesUntilEndOfTurnEffect : ContinuousEffect
{
    private readonly Creature _target;

    public LoseAllCreatureTypesUntilEndOfTurnEffect(Creature target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public override Layer Layer => Layer.Type;

    public override bool ExpiresAtEndOfTurn => true;

    public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);

    public override void Apply(CreatureCharacteristics chars) =>
        chars.Subtypes.RemoveWhere(IsCreatureSubtype);

    /// <summary>
    /// CR 205.3m — predicate identifying creature-subtype enum members. The
    /// <see cref="CardSubtype"/> enum carries no category metadata, so the
    /// well-known non-creature subtype ranges (Land / Artifact / Enchantment /
    /// Planeswalker) are explicitly excluded. Conservative: any unknown /
    /// future value is treated as a creature subtype, matching "loses all
    /// creature types" which only references creature subtypes. Same carve-out
    /// shape as Stoneforge Masterwork.
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
