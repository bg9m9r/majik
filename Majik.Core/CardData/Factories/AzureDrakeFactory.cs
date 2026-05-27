using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Azure Drake (Portal / Portal Second Age, {3}{U}).
///
/// Creature — Drake 2/4. Oracle text:
///   "Flying"
///
/// A 2/4 evasive blue flier for four mana — Azure Drake is a defensive
/// Drake that trades the aggression of Snapping Drake (3/2) for a tougher
/// body (2/4), making it more resilient in combat while still carrying the
/// Flying evasion keyword. It fits into tempo and control strategies that
/// want a blocker in the air.
/// Azure Drake is purely a vanilla flier: no triggers, no activated
/// abilities, just the printed Flying keyword (CR 702.9).
///
/// ## Implementation
///
/// - 2/4 <see cref="Creature"/> with <see cref="CardSubtype.Drake"/>,
///   mana cost {3}{U} (mana value 4, blue — CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat block-restriction path reads the marker directly —
///   same shape as <see cref="SnappingDrakeFactory"/>'s Flying.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Azure Drake")]
public static class AzureDrakeFactory
{
    public const string CardName = "Azure Drake";
    public const string PrintedManaCost = "{3}{U}";
    public const int Power = 2;
    public const int Toughness = 4;

    /// <summary>
    /// Constructs Azure Drake — a {3}{U} 2/4 Creature — Drake with the
    /// Flying keyword marker.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Drake });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Block restrictions enforced by
        // CombatRules / CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        return card;
    }
}
