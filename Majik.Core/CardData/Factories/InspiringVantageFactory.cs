using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Inspiring Vantage (Kaladesh fastland cycle).
///
/// R/W fastland. Oracle text:
///   "This land enters tapped unless you control two or fewer other lands.
///    {T}: Add {R} or {W}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/inspiring-vantage.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Two mana
/// abilities only — the conditional ETB-tapped is handled by the binder
/// layer in the production path. Same Kaladesh fastland cycle as
/// Concealed Courtyard, Spirebluff Canal, Botanical Sanctum, Blooming
/// Marsh.
///
/// ## Implemented elsewhere
/// - <b>Conditional ETB-tapped ("two or fewer other lands")</b>: handled
///   at the binder layer
///   (<see cref="Majik.Core.CardData.ConditionalEntersTappedBinder"/>),
///   which already matches the "N or fewer / more other lands" form
///   shared with the Kamigawa channel lands. Production card-load path
///   via <see cref="Majik.Core.CardData.ScryfallCardFactory"/>; this
///   named-card factory builds the land without the replacement (test
///   convenience).
/// </summary>
public static class InspiringVantageFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("inspiring-vantage");

    /// <summary>
    /// Construct Inspiring Vantage owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
