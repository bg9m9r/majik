using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Battlefield Raptor (Modern Horizons, {W}).
///
/// Creature — Bird 1/2. Oracle text:
///   "Flying, first strike"
///
/// ## Implementation
///
/// - {W} 1/2 <see cref="Creature"/> — Bird, mana value 1, white
///   (CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b> and <b>First strike (CR 702.7)</b> are carried
///   as the JSON definition's <c>keywords</c> entries;
///   <see cref="CardDefinitionFactory"/> attaches each as a plain
///   <see cref="Majik.Core.Abilities.KeywordAbility"/> marker. The combat
///   block-restriction path reads the Flying marker directly, and
///   <see cref="Majik.Core.Combat.CombatAbilities"/>'s <c>HasFirstStrike</c>
///   ("First strike") drives the first-strike combat damage step.
///
/// A clean keyword-only flier — no triggers, no activated abilities. The JSON
/// definition fully describes the card; this factory just loads it and builds
/// through <see cref="CardDefinitionFactory"/> (same pattern as
/// <see cref="KnightOfMeadowgrainFactory"/>).
/// </summary>
[CardName("Battlefield Raptor")]
public static class BattlefieldRaptorFactory
{
    public const string CardName = "Battlefield Raptor";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("battlefield-raptor");

    /// <summary>
    /// Constructs Battlefield Raptor — a {W} 1/2 Creature — Bird with the
    /// Flying and First strike keyword markers.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
