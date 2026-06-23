using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Horned Turtle ({2}{U}). Creature — Turtle 1/4.
/// Oracle text (verified against the embedded Modern seed 2026-06):
/// empty — a vanilla blue defensive two-drop-plus (mana value 3) with a high
/// toughness and no printed keywords, triggers, statics, or activated
/// abilities.
///
/// The card's entire shape (name, Creature type, Turtle subtype, {2}{U}, 1/4)
/// is materialised from the embedded JSON definition (<c>horned-turtle.json</c>)
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
[CardName("Horned Turtle")]
public static class HornedTurtleFactory
{
    public const string CardName = "Horned Turtle";
    public const string Slug = "horned-turtle";
    public const int Power = 1;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Horned Turtle from its embedded JSON definition. The card is
    /// fully shaped (name, Creature — Turtle, {2}{U}, 1/4, owner/controller) by
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
