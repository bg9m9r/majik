using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Haunted Ridge (Innistrad: Midnight Hunt B/R
/// "slow land" cycle).
///
/// Oracle text:
///   "This land enters tapped unless you control two or more other lands.
///    {T}: Add {B} or {R}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/haunted-ridge.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Two mana
/// abilities only ({B} + {R}) — the conditional ETB-tapped is handled by
/// the binder layer in the production card-load path. Same shape as the
/// Kaladesh fastland factories (Blooming Marsh, Inspiring Vantage, …),
/// only the produced colours and the ETB threshold/direction differ.
///
/// ## Implemented elsewhere
/// - <b>Conditional ETB-tapped ("two or more other lands", CR 614.1c)</b>:
///   handled at the binder layer
///   (<see cref="Majik.Core.CardData.ConditionalEntersTappedBinder"/>),
///   which already matches the "N or [more|fewer] other lands" form
///   (here: threshold 2, direction "more"). Production card-load path via
///   <see cref="Majik.Core.CardData.ScryfallCardFactory"/>; this
///   named-card factory builds the land without the replacement (test
///   convenience — matches Blooming Marsh's posture).
/// </summary>
[CardName("Haunted Ridge")]
public static class HauntedRidgeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("haunted-ridge");

    /// <summary>
    /// Construct Haunted Ridge owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
