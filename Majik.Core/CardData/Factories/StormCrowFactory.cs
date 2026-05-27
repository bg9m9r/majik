using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Storm Crow (Portal, {1}{U}).
///
/// Creature — Bird 1/2. Oracle text:
///   "Flying"
///
/// A 1/2 evasive blue flier for two mana — Storm Crow is an iconic
/// (if modest) vanilla blue Bird, beloved for its meme status and
/// lightweight evasion. Storm Crow is purely a vanilla flier: no triggers,
/// no activated abilities, just the printed Flying keyword (CR 702.9).
///
/// ## Implementation
///
/// - 1/2 <see cref="Creature"/> with <see cref="CardSubtype.Bird"/>,
///   mana cost {1}{U} (mana value 2, blue — CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat block-restriction path reads the marker directly —
///   same shape as <see cref="WindDrakeFactory"/>'s Flying.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Storm Crow")]
public static class StormCrowFactory
{
    public const string CardName = "Storm Crow";
    public const string PrintedManaCost = "{1}{U}";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Storm Crow — a {1}{U} 1/2 Creature — Bird with the
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
            subtypes: new[] { CardSubtype.Bird });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Block restrictions enforced by
        // CombatRules / CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        return card;
    }
}
