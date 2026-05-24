using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dryad Arbor (Future Sight).
///
/// Dryad Arbor is a Land Creature — Forest Dryad 1/1 with no mana cost
/// (CR 305.8). The {T}: Add {G} ability is wired explicitly because
/// Dryad Arbor lacks the Basic supertype, so OracleManaBinder's
/// basic-land mana hook doesn't apply.
///
/// Now a thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/dryad-arbor.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Multi-type
/// dispatch (Creature primary, Land secondary) is handled by the factory.
///
/// ## Deferred
/// - Land-drop-per-turn enforcement (no land-play restriction yet).
/// - Summoning sickness (creatures that enter as lands vs. creatures —
///   CR 302.6).
/// - Green Sun's Zenith interaction (can be fetched as a Forest
///   creature — deferred to the targeting / land-subtype search slice).
/// </summary>
[CardName("Dryad Arbor")]
public static class DryadArborFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("dryad-arbor");

    /// <summary>
    /// Construct a Dryad Arbor for the given owner. The returned
    /// <see cref="Creature"/> also carries <see cref="Cards.Types.CardType.Land"/>
    /// (multi-type — CR 305.8 / 302.1) and a {T}: Add {G} mana ability.
    /// </summary>
    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
