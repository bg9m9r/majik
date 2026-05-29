using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Copperline Gorge (Scars of Mirrodin fastland cycle).
///
/// R/G fastland. Oracle text:
///   "This land enters tapped unless you control two or fewer other lands.
///    {T}: Add {R} or {G}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/copperline-gorge.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Two mana
/// abilities only — the conditional ETB-tapped is handled by the binder
/// layer in the production path. Same fastland cycle as Spirebluff Canal,
/// Inspiring Vantage, Concealed Courtyard, Botanical Sanctum, Blooming Marsh.
///
/// ## Implemented elsewhere
/// - <b>Conditional ETB-tapped ("two or fewer other lands")</b>: handled
///   at the binder layer
///   (<see cref="Majik.Core.CardData.ConditionalEntersTappedBinder"/>),
///   which already matches the "N or fewer / more other lands" form
///   (Rule 614 replacement effect — the land's controller chooses nothing;
///   the permanent simply enters tapped when the condition fails).
///   Production card-load path via
///   <see cref="Majik.Core.CardData.ScryfallCardFactory"/>; this named-card
///   factory builds the land without the replacement (test convenience).
/// </summary>
[CardName("Copperline Gorge")]
public static class CopperlineGorgeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("copperline-gorge");

    /// <summary>
    /// Construct Copperline Gorge owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
