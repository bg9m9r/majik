using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Copper Myr (Mirrodin, {2}).
///
/// Artifact Creature — Myr 1/1. Oracle text (verified against Scryfall):
///   "{T}: Add {G}."
///
/// The full card — name, dual <b>Artifact + Creature</b> types
/// (CR 301.1 / 302.1), Myr subtype, {2}, 1/1, and the
/// <b>{T}: Add {G}</b> mana ability (CR 605.1) — is materialised entirely
/// from the embedded JSON definition (<c>copper-myr.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON
/// <see cref="ManaAbilityDefinition"/> schema already expresses the
/// vanilla "{T}: Add &lt;produces&gt;" shape, so this card needs no
/// hand-rolled C# behaviour.
///
/// Copper Myr is the green member of the Mirrodin mana-myr cycle: the
/// exact shape of <see cref="SilverMyrFactory"/> with {U} -> {G}. (It is
/// also the {C} -> {G}, Infect-dropped delta from <see cref="PlagueMyrFactory"/>,
/// the suggested analogue.) That delta collapses cleanly into the JSON
/// definition.
/// </summary>
[CardName("Copper Myr")]
public static class CopperMyrFactory
{
    public const string CardName = "Copper Myr";
    public const string Slug = "copper-myr";

    /// <summary>
    /// Materialise Copper Myr for <paramref name="owner"/> from the
    /// embedded JSON definition. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
