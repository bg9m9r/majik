using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kalonian Tusker (Magic 2014, {G}{G}).
/// Creature — Beast 3/3. Oracle text (verified against Scryfall 2026-06):
/// empty — Kalonian Tusker is a vanilla, mono-green-cost 3/3 two-drop with
/// no printed keywords, triggers, statics, or activated abilities.
///
/// The card's entire shape (name, Creature type, Beast subtype, {G}{G}, 3/3)
/// is materialised from the embedded JSON definition (<c>kalonian-tusker.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Because the card is vanilla
/// there is no behaviour to layer on top — the factory is a thin wrapper that
/// builds the definition and wires owner/controller (handled by
/// <see cref="CardDefinitionFactory.Build"/>).
///
/// Mirrors the JSON-backed define-only path used by the rest of the vanilla
/// pool (e.g. <see cref="GrizzlyBearsFactory"/>). CR 110 — a creature is a
/// permanent; no abilities means no further rules wiring is required.
/// </summary>
[CardName("Kalonian Tusker")]
public static class KalonianTuskerFactory
{
    public const string CardName = "Kalonian Tusker";
    public const string Slug = "kalonian-tusker";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Kalonian Tusker from its embedded JSON definition. The card is
    /// fully shaped (name, Creature — Beast, {G}{G}, 3/3, owner/controller) by
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
