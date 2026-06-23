using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Yoked Ox (Theros, {W}).
/// Creature — Ox 0/4. Oracle text (verified against Scryfall 2026-06):
/// empty — Yoked Ox is a pure vanilla one-drop white wall. No printed
/// keywords, triggers, statics, or activated abilities.
///
/// The card's entire shape (name, Creature type, Ox subtype, {W}, 0/4) is
/// materialised from the embedded JSON definition (<c>yoked-ox.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Because the card is vanilla
/// there is no behaviour to layer on top — the factory is a thin wrapper that
/// builds the definition and wires owner/controller (handled by
/// <see cref="CardDefinitionFactory.Build"/>).
///
/// Mirrors the JSON-backed define-only path used by the rest of the vanilla
/// pool (see <see cref="GrizzlyBearsFactory"/>). CR 110 — a creature is a
/// permanent; no abilities means no further rules wiring is required.
/// </summary>
[CardName("Yoked Ox")]
public static class YokedOxFactory
{
    public const string CardName = "Yoked Ox";
    public const string Slug = "yoked-ox";
    public const int Power = 0;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Yoked Ox from its embedded JSON definition. The card is fully
    /// shaped (name, Creature — Ox, {W}, 0/4, owner/controller) by
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
