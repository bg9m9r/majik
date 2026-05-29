using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shipwreck Marsh (Innistrad: Midnight Hunt
/// slowland cycle).
///
/// U/B slowland. Oracle text:
///   "This land enters tapped unless you control two or more other lands.
///    {T}: Add {U} or {B}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/shipwreck-marsh.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Two mana
/// abilities only — the conditional ETB-tapped is handled by the binder
/// layer in the production path. Same Midnight Hunt slowland cycle as
/// Deathcap Glade, Dreamroot Cascade, Haunted Ridge, Rockfall Vale,
/// Stormcarved Coast, Sundown Pass, Overgrown Farmland, Deserted Beach.
///
/// ## Implemented elsewhere
/// - <b>Conditional ETB-tapped ("two or more other lands")</b> (CR 614.1c):
///   handled at the binder layer
///   (<see cref="Majik.Core.CardData.ConditionalEntersTappedBinder"/>),
///   whose regex already matches the "N or more other lands" form shared
///   with the Kamigawa channel lands. Production card-load path via
///   <see cref="Majik.Core.CardData.ScryfallCardFactory"/>; this named-card
///   factory builds the land without the replacement (test convenience).
/// </summary>
[CardName("Shipwreck Marsh")]
public static class ShipwreckMarshFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("shipwreck-marsh");

    /// <summary>
    /// Construct Shipwreck Marsh owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
