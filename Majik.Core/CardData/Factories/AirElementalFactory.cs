using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Air Elemental (Alpha / various Core Sets, {3}{U}{U}).
///
/// Creature — Elemental 4/4. Oracle text:
///   "Flying"
///
/// A 4/4 evasive blue flier for five mana — Air Elemental is one of the
/// original Alpha creatures, a premier finisher for Control and tempo
/// strategies in early Magic. It pairs flying evasion with a substantial
/// 4/4 body, making it hard to block and difficult to remove in combat.
/// Air Elemental is purely a vanilla flier: no triggers, no activated
/// abilities, just the printed Flying keyword (CR 702.9).
///
/// ## Implementation
///
/// - 4/4 <see cref="Creature"/> with <see cref="CardSubtype.Elemental"/>,
///   mana cost {3}{U}{U} (mana value 5, blue — CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat block-restriction path reads the marker directly —
///   same shape as <see cref="SnappingDrakeFactory"/>'s Flying.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Air Elemental")]
public static class AirElementalFactory
{
    public const string CardName = "Air Elemental";
    public const string PrintedManaCost = "{3}{U}{U}";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>
    /// Constructs Air Elemental — a {3}{U}{U} 4/4 Creature — Elemental with
    /// the Flying keyword marker.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elemental });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Block restrictions enforced by
        // CombatRules / CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        return card;
    }
}
