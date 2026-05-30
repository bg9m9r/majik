using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Snow-Covered Wastes.
///
/// Type line: Basic Snow Land  (no land subtype)
/// Oracle text: {T}: Add {C}.
///
/// Snow-Covered Wastes pairs the Wastes chassis with the Snow supertype. Like
/// Wastes, it has the Basic supertype but no basic land subtype (CR 205.3i lists
/// the basic land types; Wastes is not among them), so it gains no intrinsic mana
/// ability from CR 305.6 — the "{T}: Add {C}" ability is printed directly on the
/// card and is expressed explicitly as a <see cref="Majik.Core.Abilities.ManaAbility"/>
/// producing one colorless mana ({C}) in the card definition.
///
/// It additionally carries the Snow supertype (CR 205.4d), which matters for
/// cards that care about snow permanents or snow mana (e.g. Skred, Dead of
/// Winter, Rime Tender).
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/snow-covered-wastes.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card.
/// </summary>
[CardName("Snow-Covered Wastes")]
public static class SnowCoveredWastesFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("snow-covered-wastes");

    /// <summary>Construct a Snow-Covered Wastes owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
