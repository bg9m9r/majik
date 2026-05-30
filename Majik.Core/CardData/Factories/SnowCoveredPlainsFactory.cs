using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Snow-Covered Plains (Ice Age / reprints).
///
/// Type line: Basic Snow Land — Plains
/// Oracle text: ({T}: Add {W}.)
///
/// Snow-Covered Plains carries two supertypes — Basic AND Snow (CR 205.4d) —
/// plus the Plains land subtype. The {T}: Add {W} mana ability is intrinsic
/// to the Plains land subtype (CR 305.6 — a land with the Plains subtype has
/// the intrinsic ability "{T}: Add {W}"). It is expressed explicitly as a
/// <see cref="Majik.Core.Abilities.ManaAbility"/> in the card definition so
/// the card carries its mana ability when built through the data-driven
/// <see cref="CardDefinitionFactory"/> route (same chassis as
/// <see cref="PlainsFactory"/>, with the extra Snow supertype).
///
/// The Snow supertype matters for cards that care about snow permanents or
/// snow mana (e.g. Skred, Dead of Winter, Rime Tender).
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/snow-covered-plains.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card.
/// </summary>
[CardName("Snow-Covered Plains")]
public static class SnowCoveredPlainsFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("snow-covered-plains");

    /// <summary>Construct a Snow-Covered Plains owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
