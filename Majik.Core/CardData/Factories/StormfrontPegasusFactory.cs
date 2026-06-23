using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stormfront Pegasus (Magic Origins / Welcome Deck 2017,
/// {1}{W}). Creature — Pegasus 2/1. Oracle text (verified against Scryfall):
///   "Flying"
///
/// The entire card is materialised from the embedded JSON definition
/// (<c>stormfront-pegasus.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — the JSON carries the name,
/// Creature type, Pegasus subtype, {1}{W} cost, 2/1 body, and the Flying
/// keyword (CR 702.9). There is no bespoke ability to layer on; the
/// <see cref="Majik.Core.Abilities.KeywordAbility"/> for Flying is wired by
/// <see cref="CardDefinitionFactory.Build"/> from the JSON <c>keywords</c> list.
///
/// ## Rules references
/// - CR 702.9 — Flying: can only be blocked by creatures with flying or reach.
/// - CR 202.3 — mana value of {1}{W} is 2.
/// - CR 105 — colour is derived from coloured pips; {W} makes this card white.
/// </summary>
[CardName("Stormfront Pegasus")]
public static class StormfrontPegasusFactory
{
    public const string CardName = "Stormfront Pegasus";
    public const string Slug = "stormfront-pegasus";

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Pegasus
        // subtype, {1}{W}, 2/1, Flying keyword). No abilities to layer on.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
