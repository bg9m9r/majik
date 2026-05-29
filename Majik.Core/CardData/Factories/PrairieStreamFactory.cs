using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Prairie Stream (Battle for Zendikar W/U "battle
/// land" / tango-land cycle).
///
/// Type line "Land — Plains Island". Oracle text:
///   "({T}: Add {W} or {U}.)
///    This land enters tapped unless you control two or more basic lands."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/prairie-stream.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card: the two
/// produced colours ({W} / {U}) plus the printed Plains + Island
/// subtypes. Same five-card BFZ battle-land cycle as Sunken Hollow,
/// Smoldering Marsh, Cinder Glade, Canopy Vista.
///
/// ## Implemented elsewhere
/// - <b>Conditional ETB-tapped ("two or more basic lands", CR 614.1c)</b>:
///   a <see cref="Majik.Core.Effects.ConditionalEntersTappedReplacement"/>
///   belongs to the binder layer on the production card-load path
///   (<see cref="Majik.Core.CardData.ScryfallCardFactory"/>), not to this
///   thin named-card factory — same shape-only posture as
///   <see cref="BloomingMarshFactory"/> and the M10/Innistrad
///   <see cref="CheckLandCycleFactory"/> single-arg dispatch path. This
///   factory builds the land without the replacement (test convenience /
///   identity + mana abilities only).
/// </summary>
[CardName("Prairie Stream")]
public static class PrairieStreamFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("prairie-stream");

    /// <summary>
    /// Construct Prairie Stream owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
