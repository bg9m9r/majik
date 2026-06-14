using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Knight of Meadowgrain (Lorwyn, {W}{W}).
///
/// Creature — Kithkin Knight 2/2. Oracle text:
///   "First strike
///    Lifelink"
///
/// ## Implementation
///
/// - {W}{W} 2/2 <see cref="Creature"/> — Kithkin Knight, mana value 2,
///   white (CR 202.3 / CR 105.1).
/// - <b>First strike (CR 702.7)</b> and <b>Lifelink (CR 702.15)</b> are
///   carried as the JSON definition's <c>keywords</c> entries;
///   <see cref="CardDefinitionFactory"/> attaches each as a plain
///   <see cref="Majik.Core.Abilities.KeywordAbility"/> marker.
///   <see cref="Majik.Core.Combat.CombatAbilities"/> consumes the markers:
///   <c>HasFirstStrike</c> ("First strike") drives the first-strike combat
///   damage step, and <c>HasLifelink</c> ("Lifelink") routes combat damage
///   into life gain for the controller.
///
/// A clean keyword-only creature — no triggers, no activated abilities.
/// The JSON definition fully describes the card; this factory just loads it
/// and builds through <see cref="CardDefinitionFactory"/> (the same pattern
/// as <see cref="CollectorOupheFactory"/>, minus the post-build static).
/// </summary>
[CardName("Knight of Meadowgrain")]
public static class KnightOfMeadowgrainFactory
{
    public const string CardName = "Knight of Meadowgrain";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("knight-of-meadowgrain");

    /// <summary>
    /// Constructs Knight of Meadowgrain — a {W}{W} 2/2 Creature — Kithkin
    /// Knight with First strike and Lifelink keyword markers.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
