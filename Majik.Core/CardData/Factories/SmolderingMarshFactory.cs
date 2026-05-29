using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Smoldering Marsh (Battle for Zendikar B/R
/// "battle land" / "tango land" cycle). Oracle text (Scryfall, verified):
///   "({T}: Add {B} or {R}.)
///    This land enters tapped unless you control two or more basic lands."
/// Type line: "Land — Swamp Mountain".
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/smoldering-marsh.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card: a nonbasic
/// Land carrying the two printed land subtypes (Swamp / Mountain) plus two
/// mana abilities {B} and {R} (CR 605.1 — mana abilities don't use the
/// stack). Same posture as <see cref="ZagothTriomeFactory"/> (subtyped
/// nonbasic land with explicit JSON mana abilities) and
/// <see cref="BloomingMarshFactory"/> (conditional ETB-tapped deferred to
/// the binder layer). Battle-land siblings: Prairie Stream, Sunken Hollow,
/// Cinder Glade, Canopy Vista.
///
/// ## Implemented elsewhere
/// - <b>Conditional ETB-tapped ("unless you control two or more basic
///   lands", CR 614.1c)</b>: a replacement effect, not modelled here. On
///   the production card-load path it would be wired by the binder layer
///   (<see cref="Majik.Core.CardData.ConditionalEntersTappedBinder"/> and
///   friends) from the printed oracle text — this named-card factory builds
///   the land without the replacement, matching the established
///   Blooming Marsh / Zagoth Triome posture (test convenience; no triggered
///   or non-mana activated abilities ship from the named factory).
/// </summary>
[CardName("Smoldering Marsh")]
public static class SmolderingMarshFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("smoldering-marsh");

    /// <summary>
    /// Construct Smoldering Marsh owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
