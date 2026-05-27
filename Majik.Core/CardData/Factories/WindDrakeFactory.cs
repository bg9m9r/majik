using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wind Drake (Portal, {2}{U}).
///
/// Creature — Drake 2/2. Oracle text:
///   "Flying"
///
/// A 2/2 evasive blue flier for three mana — Wind Drake is the archetypal
/// vanilla blue creature, appearing in countless Core Sets and beginner
/// products. It pairs well in tempo and evasion-based strategies. Wind
/// Drake is purely a vanilla flier: no triggers, no activated abilities,
/// just the printed Flying keyword (CR 702.9).
///
/// ## Implementation
///
/// - 2/2 <see cref="Creature"/> with <see cref="CardSubtype.Drake"/>,
///   mana cost {2}{U} (mana value 3, blue — CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat block-restriction path reads the marker directly —
///   same shape as <see cref="WelkinTernFactory"/>'s Flying /
///   <see cref="OrnithopterFactory"/>'s Flying.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Wind Drake")]
public static class WindDrakeFactory
{
    public const string CardName = "Wind Drake";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Wind Drake — a {2}{U} 2/2 Creature — Drake with the
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
