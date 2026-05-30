using Majik.Core.CardData.Definitions;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hill Giant (a long-running core-set common, {3}{R}).
///
/// Creature — Giant 3/3. Oracle text (verified against Scryfall): empty —
/// Hill Giant is a vanilla creature with no printed keywords, triggers,
/// statics, or activated abilities.
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Mana cost: {3}{R}; mana value 4 (CR 202.3).</item>
///   <item>Type line: Creature — Giant; colors: R.</item>
///   <item>Power/toughness: 3/3.</item>
/// </list>
///
/// ## Implementation
/// The full card shape (name, Creature, Giant subtype, {3}{R}, 3/3) is
/// materialised from the embedded JSON definition (<c>hill-giant.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same data-backed posture as
/// the other JSON-defined factories. Because the card is vanilla, no
/// abilities are layered on top.
///
/// - <see cref="CardSubtype.Giant"/> (CR 205.3m).
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point dispatched by <see cref="NamedCardFactory"/>.
/// </summary>
[CardName("Hill Giant")]
public static class HillGiantFactory
{
    public const string CardName = "Hill Giant";
    public const string Slug = "hill-giant";

    /// <summary>
    /// Constructs Hill Giant — a vanilla {3}{R} 3/3 Creature — Giant — from
    /// its embedded JSON definition.
    /// </summary>
    public static Majik.Core.Cards.Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Majik.Core.Cards.Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
