using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Razorverge Thicket (Mirrodin Besieged fastland cycle).
///
/// G/W fastland. Oracle text:
///   "This land enters tapped unless you control two or fewer other lands.
///    {T}: Add {G} or {W}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/razorverge-thicket.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Two mana
/// abilities only — the conditional ETB-tapped is handled by the binder
/// layer in the production path. Same Mirrodin Besieged fastland cycle as
/// the Kaladesh fastlands (Spirebluff Canal, Inspiring Vantage, Concealed
/// Courtyard, Botanical Sanctum, Blooming Marsh).
///
/// ## Implemented elsewhere
/// - <b>Conditional ETB-tapped ("two or fewer other lands")</b> (CR 614.1c):
///   handled at the binder layer
///   (<see cref="Majik.Core.CardData.ConditionalEntersTappedBinder"/>),
///   whose regex already matches the "N or fewer / more other lands" form
///   shared with the Kaladesh fastlands + Kamigawa channel lands.
///   Production card-load path via
///   <see cref="Majik.Core.CardData.ScryfallCardFactory"/>; this named-card
///   factory builds the land without the replacement (test convenience).
/// </summary>
[CardName("Razorverge Thicket")]
public static class RazorvergeThicketFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("razorverge-thicket");

    /// <summary>
    /// Construct Razorverge Thicket owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
