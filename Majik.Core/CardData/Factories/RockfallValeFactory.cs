using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rockfall Vale (Innistrad: Crimson Vow "slow
/// land" cycle).
///
/// R/G slow land. Oracle text:
///   "This land enters tapped unless you control two or more other lands.
///    {T}: Add {R} or {G}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/rockfall-vale.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Two mana
/// abilities only — the conditional ETB-tapped is handled by the binder
/// layer in the production path. Same Innistrad: Midnight Hunt / Crimson
/// Vow slow-land cycle as Deserted Beach, Shipwreck Marsh, Haunted Ridge,
/// Overgrown Farmland (and the structurally identical Streets of New
/// Capenna / Murders at Karlov Manor families).
///
/// ## Implemented elsewhere
/// - <b>Conditional ETB-tapped ("two or more other lands")</b>: handled
///   at the binder layer
///   (<see cref="Majik.Core.CardData.ConditionalEntersTappedBinder"/>),
///   whose regex already matches the "N or [more|fewer] other lands" form
///   shared with the Kaladesh fastlands and Kamigawa channel lands
///   (CR 614.1c — a replacement effect, not a trigger). The "two or more"
///   direction yields untapped iff the controller already has >= 2 other
///   lands. Wired on the production card-load path; this named-card
///   factory builds the land without the replacement (test convenience),
///   mirroring <see cref="BloomingMarshFactory"/>.
/// </summary>
[CardName("Rockfall Vale")]
public static class RockfallValeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("rockfall-vale");

    /// <summary>
    /// Construct Rockfall Vale owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
