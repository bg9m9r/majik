using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vizzerdrix (Portal Three Kingdoms / reprints, {6}{U}).
/// Creature — Rabbit Beast 6/6. Oracle text (verified against Scryfall 2026-06):
/// empty — Vizzerdrix is a vanilla blue beatstick (the over-costed "giant
/// killer rabbit"). No printed keywords, triggers, statics, or activated
/// abilities. Mono-blue (single {U} pip; CR 105.2). Mana value 7.
///
/// The card's entire shape (name, Creature type, Rabbit/Beast subtypes,
/// {6}{U}, 6/6) is materialised from the embedded JSON definition
/// (<c>vizzerdrix.json</c>) via
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
[CardName("Vizzerdrix")]
public static class VizzerdrixFactory
{
    public const string CardName = "Vizzerdrix";
    public const string Slug = "vizzerdrix";
    public const int Power = 6;
    public const int Toughness = 6;

    /// <summary>
    /// Construct Vizzerdrix from its embedded JSON definition. The card is fully
    /// shaped (name, Creature — Rabbit Beast, {6}{U}, 6/6, owner/controller) by
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
