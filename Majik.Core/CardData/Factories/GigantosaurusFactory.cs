using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gigantosaurus (Dominaria, {G}{G}{G}{G}{G}).
/// Creature — Dinosaur 10/10. Oracle text (verified against Scryfall 2026-06):
/// empty — Gigantosaurus is a vanilla beater whose only distinguishing feature
/// is the five-pip mono-green cost and its outsized 10/10 body. No printed
/// keywords, triggers, statics, or activated abilities.
///
/// The card's entire shape (name, Creature type, Dinosaur subtype,
/// {G}{G}{G}{G}{G}, 10/10) is materialised from the embedded JSON definition
/// (<c>gigantosaurus.json</c>) via
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
[CardName("Gigantosaurus")]
public static class GigantosaurusFactory
{
    public const string CardName = "Gigantosaurus";
    public const string Slug = "gigantosaurus";
    public const int Power = 10;
    public const int Toughness = 10;

    /// <summary>
    /// Construct Gigantosaurus from its embedded JSON definition. The card is
    /// fully shaped (name, Creature — Dinosaur, {G}{G}{G}{G}{G}, 10/10,
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
