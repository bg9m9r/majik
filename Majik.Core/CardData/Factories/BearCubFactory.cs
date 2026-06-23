using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bear Cub ({1}{G}). Creature — Bear 2/2.
/// Oracle text (verified against Scryfall 2026-06): empty — Bear Cub is a
/// functional reprint of <see cref="GrizzlyBearsFactory">Grizzly Bears</see>,
/// the proverbial vanilla two-drop. No printed keywords, triggers, statics, or
/// activated abilities.
///
/// The card's entire shape (name, Creature type, Bear subtype, {1}{G}, 2/2) is
/// materialised from the embedded JSON definition (<c>bear-cub.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Because the card is vanilla there
/// is no behaviour to layer on top — the factory is a thin wrapper that builds
/// the definition and wires owner/controller (handled by
/// <see cref="CardDefinitionFactory.Build"/>). CR 110 — a creature is a
/// permanent; no abilities means no further rules wiring is required.
/// </summary>
[CardName("Bear Cub")]
public static class BearCubFactory
{
    public const string CardName = "Bear Cub";
    public const string Slug = "bear-cub";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Bear Cub from its embedded JSON definition. The card is fully
    /// shaped (name, Creature — Bear, {1}{G}, 2/2, owner/controller) by
    /// <see cref="CardDefinitionFactory.Build"/>; there is no ability to layer
    /// on. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
