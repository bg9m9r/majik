using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Brightblade Stoat (Bloomburrow, {1}{W}).
///
/// Creature — Weasel Soldier 2/2. Oracle text:
///   "First strike, lifelink"
///
/// ## Implementation
///
/// - {1}{W} 2/2 <see cref="Creature"/> — Weasel Soldier, mana value 2,
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
/// A clean keyword-only creature — no triggers, no activated abilities. The
/// same shape as <see cref="KnightOfMeadowgrainFactory"/> (the suggested
/// analogue), differing only in mana cost ({1}{W} vs {W}{W}) and subtypes.
/// The JSON definition fully describes the card; this factory just loads it
/// and builds through <see cref="CardDefinitionFactory"/>.
/// </summary>
[CardName("Brightblade Stoat")]
public static class BrightbladeStoatFactory
{
    public const string CardName = "Brightblade Stoat";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("brightblade-stoat");

    /// <summary>
    /// Constructs Brightblade Stoat — a {1}{W} 2/2 Creature — Weasel Soldier
    /// with First strike and Lifelink keyword markers.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
