using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Alpha Myr (Mirrodin, {2}).
///
/// Artifact Creature — Myr 2/1. Oracle text (verified against Scryfall):
/// empty — Alpha Myr is a vanilla artifact creature. Unlike the rest of the
/// Mirrodin Myr cycle (Gold / Silver / Iron / Copper / Leaden), it has no
/// mana ability; it is simply a {2} 2/1 artifact body with no printed
/// keywords, triggers, statics, or activated abilities.
///
/// The card's entire shape (name, dual Creature + Artifact type, Myr subtype,
/// {2}, 2/1) is materialised from the embedded JSON definition
/// (<c>alpha-myr.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON <c>types</c> array
/// carries both Creature and Artifact, so <see cref="Card.HasType"/> surfaces
/// the artifact type for affinity / artifact-matters consumers
/// (CR 301.1 / 302.1). Because the card is vanilla there is no behaviour to
/// layer on top — the factory is a thin wrapper that builds the definition and
/// lets <see cref="CardDefinitionFactory.Build"/> wire owner/controller
/// (CR 110 — a creature is a permanent; no abilities means no further rules
/// wiring is required).
/// </summary>
[CardName("Alpha Myr")]
public static class AlphaMyrFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("alpha-myr");

    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
