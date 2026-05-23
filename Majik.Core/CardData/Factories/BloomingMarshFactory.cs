using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Blooming Marsh (Kaladesh fastland cycle).
///
/// B/G fastland. Oracle text:
///   "This land enters tapped unless you control two or fewer other lands.
///    {T}: Add {B} or {G}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/blooming-marsh.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Two mana
/// abilities only — the conditional ETB-tapped is handled by the binder
/// layer in the production path. Same Kaladesh fastland cycle as
/// Inspiring Vantage, Spirebluff Canal, Concealed Courtyard, Botanical
/// Sanctum.
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
public static class BloomingMarshFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("blooming-marsh");

    /// <summary>
    /// Construct Blooming Marsh owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
