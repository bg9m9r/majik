using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Earth Elemental (Alpha and many reprints, {3}{R}{R}).
/// Creature — Elemental 4/5. Oracle text (verified against Scryfall 2026-06):
/// empty — Earth Elemental is a vanilla red beatstick, 4 power / 5 toughness
/// for three generic and two Red mana (mana value 5). No printed keywords,
/// triggers, statics, or activated abilities.
///
/// The card's entire shape (name, Creature type, Elemental subtype, {3}{R}{R},
/// 4/5) is materialised from the embedded JSON definition
/// (<c>earth-elemental.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Because the card is vanilla there
/// is no behaviour to layer on top — the factory is a thin wrapper that builds
/// the definition and wires owner/controller (handled by
/// <see cref="CardDefinitionFactory.Build"/>).
///
/// Mirrors the JSON-backed define-only path used by the rest of the vanilla
/// pool (e.g. <see cref="GrizzlyBearsFactory"/>). CR 110 — a creature is a
/// permanent; no abilities means no further rules wiring is required.
/// </summary>
[CardName("Earth Elemental")]
public static class EarthElementalFactory
{
    public const string CardName = "Earth Elemental";
    public const string Slug = "earth-elemental";
    public const int Power = 4;
    public const int Toughness = 5;

    /// <summary>
    /// Construct Earth Elemental from its embedded JSON definition. The card is
    /// fully shaped (name, Creature — Elemental, {3}{R}{R}, 4/5,
    /// owner/controller) by <see cref="CardDefinitionFactory.Build"/>; there is
    /// no ability to layer on. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
