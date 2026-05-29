using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dreamroot Cascade (Wilds of Eldraine G/U
/// "slow land" cycle). Oracle text (Scryfall, verified):
///   "This land enters tapped unless you control two or more other lands.
///    {T}: Add {G} or {U}."
/// Type line: "Land" (no land subtypes).
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/dreamroot-cascade.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card: a nonbasic
/// Land carrying two mana abilities {G} and {U} (CR 605.1 — mana abilities
/// don't use the stack). Same posture as <see cref="HedgeMazeFactory"/>
/// (G/U dual land with explicit JSON mana abilities) and
/// <see cref="BloomingMarshFactory"/> (conditional ETB-tapped deferred to
/// the binder layer). Slow-land siblings share the
/// "two or more other lands" clause.
///
/// ## Implemented elsewhere
/// - <b>Conditional ETB-tapped ("unless you control two or more other
///   lands", CR 614.1c)</b>: a replacement effect, not modelled here. On
///   the production card-load path it is wired by
///   <see cref="Majik.Core.CardData.ConditionalEntersTappedBinder"/>, whose
///   regex already matches the "N or more other lands" form (direction
///   "more" — enters untapped once the controller has >= 2 other lands).
///   This named-card factory builds the land without the replacement,
///   matching the established Blooming Marsh / Smoldering Marsh posture
///   (test convenience; no triggered or non-mana activated abilities ship
///   from the named factory).
/// </summary>
[CardName("Dreamroot Cascade")]
public static class DreamrootCascadeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("dreamroot-cascade");

    /// <summary>
    /// Construct Dreamroot Cascade owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
