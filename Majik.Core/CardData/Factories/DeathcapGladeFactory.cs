using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Deathcap Glade (Innistrad: Midnight Hunt slowland cycle).
///
/// B/G slowland. Oracle text:
///   "This land enters tapped unless you control two or more other lands.
///    {T}: Add {B} or {G}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/deathcap-glade.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Two mana
/// abilities only — the conditional ETB-tapped is handled by the binder
/// layer in the production path. Same Midnight Hunt slowland cycle as
/// Dreamroot Cascade, Haunted Ridge, Rockfall Vale, Stormcarved Coast.
///
/// ## Implemented elsewhere
/// - <b>Conditional ETB-tapped ("two or more other lands")</b>: handled
///   at the binder layer
///   (<see cref="Majik.Core.CardData.ConditionalEntersTappedBinder"/>),
///   whose regex already matches the "N or more / fewer other lands" form
///   (Rule 614 replacement effect — the land enters tapped as a
///   self-replacement based on the number of OTHER lands its controller
///   controls). Production card-load path via
///   <see cref="Majik.Core.CardData.ScryfallCardFactory"/>; this
///   named-card factory builds the land without the replacement (test
///   convenience).
/// </summary>
[CardName("Deathcap Glade")]
public static class DeathcapGladeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("deathcap-glade");

    /// <summary>
    /// Construct Deathcap Glade owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
