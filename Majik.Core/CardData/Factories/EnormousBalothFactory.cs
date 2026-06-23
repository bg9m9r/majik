using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Enormous Baloth ({6}{G}).
/// Creature — Beast 7/7. Oracle text (verified against Scryfall 2026-06):
/// empty — Enormous Baloth is a vanilla green fatty, a 7/7 for seven mana.
/// No printed keywords, triggers, statics, or activated abilities.
///
/// The card's entire shape (name, Creature type, Beast subtype, {6}{G}, 7/7)
/// is materialised from the embedded JSON definition
/// (<c>enormous-baloth.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Because the card is vanilla
/// there is no behaviour to layer on top — the factory is a thin wrapper that
/// builds the definition and wires owner/controller (handled by
/// <see cref="CardDefinitionFactory.Build"/>).
///
/// Mirrors the JSON-backed define-only path used by the rest of the vanilla
/// pool (e.g. <see cref="GrizzlyBearsFactory"/>). CR 110 — a creature is a
/// permanent; no abilities means no further rules wiring is required.
/// </summary>
[CardName("Enormous Baloth")]
public static class EnormousBalothFactory
{
    public const string CardName = "Enormous Baloth";
    public const string Slug = "enormous-baloth";
    public const int Power = 7;
    public const int Toughness = 7;

    /// <summary>
    /// Construct Enormous Baloth from its embedded JSON definition. The card is
    /// fully shaped (name, Creature — Beast, {6}{G}, 7/7, owner/controller) by
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
