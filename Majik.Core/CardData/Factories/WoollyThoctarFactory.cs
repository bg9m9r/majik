using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Woolly Thoctar (Shards of Alara, {R}{G}{W}).
/// Creature — Beast 5/4. Oracle text (verified against Scryfall 2026-06):
/// empty — Woolly Thoctar is a vanilla Naya beater, three colours of mana for
/// an aggressively over-statted body and nothing else. No printed keywords,
/// triggers, statics, or activated abilities.
///
/// The card's entire shape (name, Creature type, Beast subtype, {R}{G}{W}, 5/4)
/// is materialised from the embedded JSON definition (<c>woolly-thoctar.json</c>)
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
[CardName("Woolly Thoctar")]
public static class WoollyThoctarFactory
{
    public const string CardName = "Woolly Thoctar";
    public const string Slug = "woolly-thoctar";
    public const int Power = 5;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Woolly Thoctar from its embedded JSON definition. The card is
    /// fully shaped (name, Creature — Beast, {R}{G}{W}, 5/4, owner/controller)
    /// by <see cref="CardDefinitionFactory.Build"/>; there is no ability to
    /// layer on. This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
