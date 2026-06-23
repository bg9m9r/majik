using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Yargle, Glutton of Urborg (Dominaria, {4}{B}).
/// Legendary Creature — Frog Spirit 9/3. Oracle text (verified against
/// Scryfall 2026-06): empty — Yargle is a vanilla beater whose entire flavour
/// is its absurd 9/3 body. No printed keywords, triggers, statics, or
/// activated abilities.
///
/// The card's entire shape (name, Legendary supertype, Creature type,
/// Frog/Spirit subtypes, {4}{B}, 9/3) is materialised from the embedded JSON
/// definition (<c>yargle-glutton-of-urborg.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Because the card is vanilla there
/// is no behaviour to layer on top — the factory is a thin wrapper that builds
/// the definition and wires owner/controller (handled by
/// <see cref="CardDefinitionFactory.Build"/>).
///
/// Mirrors the JSON-backed define-only path used by the rest of the vanilla
/// pool (e.g. <see cref="GrizzlyBearsFactory"/>). CR 110 — a creature is a
/// permanent; no abilities means no further rules wiring is required. The
/// Legendary supertype is fully data-driven and triggers the CR 704.5j legend
/// rule via state-based actions like any other legendary permanent.
/// </summary>
[CardName("Yargle, Glutton of Urborg")]
public static class YargleGluttonOfUrborgFactory
{
    public const string CardName = "Yargle, Glutton of Urborg";
    public const string Slug = "yargle-glutton-of-urborg";
    public const int Power = 9;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Yargle from its embedded JSON definition. The card is fully
    /// shaped (name, Legendary, Creature — Frog Spirit, {4}{B}, 9/3,
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
