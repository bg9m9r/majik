using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fire Elemental (Alpha and many reprints, {3}{R}{R}).
/// Creature — Elemental 5/4. Oracle text (verified against the embedded Modern
/// seed, Scryfall id dc506f58-048d-49cc-ad8c-2eb851b08bb6): empty — a vanilla
/// red beatstick with no printed keywords, triggers, statics, or activated
/// abilities.
///
/// The card's entire shape (name, Creature type, Elemental subtype, {3}{R}{R},
/// 5/4) is materialised from the embedded JSON definition
/// (<c>fire-elemental.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Because the card is vanilla there
/// is no behaviour to layer on top — the factory is a thin wrapper that builds
/// the definition and wires owner/controller (handled by
/// <see cref="CardDefinitionFactory.Build"/>).
///
/// Mirrors the JSON-backed define-only path used by the rest of the vanilla
/// pool (e.g. <see cref="GrizzlyBearsFactory"/>). CR 110.1 — a creature is a
/// permanent; with no abilities no further rules wiring is required.
/// </summary>
[CardName("Fire Elemental")]
public static class FireElementalFactory
{
    public const string CardName = "Fire Elemental";
    public const string Slug = "fire-elemental";
    public const int Power = 5;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Fire Elemental from its embedded JSON definition. The card is
    /// fully shaped (name, Creature — Elemental, {3}{R}{R}, 5/4,
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
