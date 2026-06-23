using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Aerial Responder (Kaladesh, {1}{W}{W}).
///
/// Creature — Dwarf Soldier 2/3. Oracle text (verified against Scryfall):
///   "Flying, vigilance, lifelink"
///
/// ## Implementation
///
/// - {1}{W}{W} 2/3 <see cref="Creature"/> — Dwarf Soldier, mana value 3,
///   white (CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b>, <b>Vigilance (CR 702.20)</b> and
///   <b>Lifelink (CR 702.15)</b> are carried as the JSON definition's
///   <c>keywords</c> entries; <see cref="CardDefinitionFactory"/> attaches
///   each as a plain <see cref="Majik.Core.Abilities.KeywordAbility"/> marker.
///   <see cref="Majik.Core.Combat.CombatAbilities"/> consumes the markers:
///   <c>HasFlying</c> drives block restrictions, <c>HasVigilance</c> keeps
///   the creature untapped when it attacks (CR 508.1f), and
///   <c>HasLifelink</c> routes combat damage into life gain for the controller.
///
/// A clean french-vanilla flier — no triggers, no activated abilities. The
/// JSON definition fully describes the card; this factory just loads it and
/// builds through <see cref="CardDefinitionFactory"/> (the same pattern as
/// <see cref="KnightOfMeadowgrainFactory"/>).
/// </summary>
[CardName("Aerial Responder")]
public static class AerialResponderFactory
{
    public const string CardName = "Aerial Responder";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("aerial-responder");

    /// <summary>
    /// Constructs Aerial Responder — a {1}{W}{W} 2/3 Creature — Dwarf Soldier
    /// with Flying, Vigilance and Lifelink keyword markers.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
