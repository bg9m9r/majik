using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Blackcleave Cliffs (Scars of Mirrodin fastland cycle).
///
/// B/R fastland. Oracle text:
///   "This land enters tapped unless you control two or fewer other lands.
///    {T}: Add {B} or {R}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/blackcleave-cliffs.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Two mana
/// abilities only — the conditional ETB-tapped is handled by the binder
/// layer in the production path. Same Scars of Mirrodin fastland cycle as
/// Copperline Gorge, Darkslick Shores, Razorverge Thicket, Seachrome Coast.
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
[CardName("Blackcleave Cliffs")]
public static class BlackcleaveCliffsFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("blackcleave-cliffs");

    /// <summary>
    /// Construct Blackcleave Cliffs owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
