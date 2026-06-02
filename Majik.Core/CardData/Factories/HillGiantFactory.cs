using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hill Giant (Alpha … 9th Edition, {3}{R}).
///
/// Creature — Giant 3/3. Oracle text (verified against Scryfall): empty —
/// Hill Giant is a French-vanilla creature with NO printed keywords,
/// triggered, activated, or static abilities. It is the canonical
/// "big dumb red beater" — its only characteristic is its {3}{R} 3/3 body.
///
/// ## Implemented (v1)
/// - <b>Creature — Giant {3}{R} 3/3</b>: identity (name, mana cost, types,
///   subtypes, power/toughness) is materialised from the embedded JSON
///   definition (<c>hill-giant.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>, owner/controller wired.
///
/// No abilities are layered on top — same vanilla posture as the
/// <see cref="GiantSpiderFactory"/> / <see cref="PhantasmalBearFactory"/>
/// shells, minus their printed riders (Giant Spider's Reach, Phantasmal
/// Bear's self-sac trigger). Mana value 4 (3 generic + one red pip,
/// CR 202.3); red colour from the {R} pip (CR 105.1 / CR 202.2).
/// </summary>
[CardName("Hill Giant")]
public static class HillGiantFactory
{
    public const string CardName = "Hill Giant";
    public const string Slug = "hill-giant";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Build Hill Giant: a {3}{R} 3/3 Creature — Giant with no abilities,
    /// constructed straight from its JSON definition.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
