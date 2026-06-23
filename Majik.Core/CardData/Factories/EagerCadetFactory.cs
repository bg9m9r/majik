using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eager Cadet (Portal / 7th Edition, {W}).
/// Creature — Human Soldier 1/1. Oracle text (verified against Scryfall
/// 2026-06): empty — a vanilla white one-drop with no printed keywords,
/// triggers, statics, or activated abilities.
///
/// The card's entire shape (name, Creature type, Human + Soldier subtypes,
/// {W}, 1/1) is materialised from the embedded JSON definition
/// (<c>eager-cadet.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Because the card is vanilla
/// there is no behaviour to layer on top — the factory is a thin wrapper that
/// builds the definition and wires owner/controller (handled by
/// <see cref="CardDefinitionFactory.Build"/>). CR 110 — a creature is a
/// permanent; no abilities means no further rules wiring is required.
///
/// Mirrors the JSON-backed define-only path used by the rest of the vanilla
/// pool (e.g. <see cref="GrizzlyBearsFactory"/>).
/// </summary>
[CardName("Eager Cadet")]
public static class EagerCadetFactory
{
    public const string CardName = "Eager Cadet";
    public const string Slug = "eager-cadet";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Eager Cadet from its embedded JSON definition. The card is
    /// fully shaped (name, Creature — Human Soldier, {W}, 1/1,
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
