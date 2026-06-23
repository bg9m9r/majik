using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Silvercoat Lion (Core sets / Origins, {1}{W}).
/// Creature — Cat 2/2. Oracle text (verified against Scryfall 2026-06):
/// empty — Silvercoat Lion is white's proverbial vanilla two-drop, the {1}{W}
/// counterpart to <see cref="GrizzlyBearsFactory"/>'s {1}{G} 2/2. No printed
/// keywords, triggers, statics, or activated abilities.
///
/// The card's entire shape (name, Creature type, Cat subtype, {1}{W}, 2/2) is
/// materialised from the embedded JSON definition (<c>silvercoat-lion.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Because the card is vanilla there
/// is no behaviour to layer on top — the factory is a thin wrapper that builds
/// the definition and wires owner/controller (handled by
/// <see cref="CardDefinitionFactory.Build"/>).
///
/// Mirrors the JSON-backed define-only path used by the rest of the vanilla
/// pool (e.g. <see cref="GrizzlyBearsFactory"/>). CR 110 — a creature is a
/// permanent; no abilities means no further rules wiring is required.
/// </summary>
[CardName("Silvercoat Lion")]
public static class SilvercoatLionFactory
{
    public const string CardName = "Silvercoat Lion";
    public const string Slug = "silvercoat-lion";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Silvercoat Lion from its embedded JSON definition. The card is
    /// fully shaped (name, Creature — Cat, {1}{W}, 2/2, owner/controller) by
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
