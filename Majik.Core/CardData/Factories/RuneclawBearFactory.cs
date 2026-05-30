using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Runeclaw Bear ({1}{G}).
///
/// Creature — Bear 2/2. Oracle text (verified against Scryfall): empty —
/// vanilla. No printed keywords, triggers, statics, or activated abilities.
///
/// The full shape (name, Creature, Bear subtype, {1}{G}, 2/2) is
/// materialised from the embedded JSON definition
/// (<c>runeclaw-bear.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Because the card is vanilla,
/// nothing is layered on top — same posture as
/// <see cref="TwinSilkSpiderFactory"/>'s base shape minus its abilities.
///
/// ## Implementation
/// - 2/2 <see cref="Creature"/> — Bear (CR 205.3m) at {1}{G}; CMC 2
///   (CR 202.3).
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point dispatched by <see cref="NamedCardFactory"/>.
/// </summary>
[CardName("Runeclaw Bear")]
public static class RuneclawBearFactory
{
    public const string CardName = "Runeclaw Bear";
    public const string Slug = "runeclaw-bear";

    /// <summary>
    /// Constructs Runeclaw Bear — a vanilla {1}{G} 2/2 Creature — Bear.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Full vanilla shape from the embedded JSON definition (name,
        // Creature, Bear subtype, {1}{G}, 2/2). The JSON carries no
        // abilities, and the card prints none.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
