using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Snapping Drake (Magic 2010 / various Core Sets, {3}{U}).
///
/// Creature — Drake 3/2. Oracle text:
///   "Flying"
///
/// A 3/2 evasive blue flier for four mana — Snapping Drake is a pushed
/// version of Wind Drake, trading one extra mana for +1/+0. It fits into
/// tempo and evasion-based strategies that want a bigger body in the air.
/// Snapping Drake is purely a vanilla flier: no triggers, no activated
/// abilities, just the printed Flying keyword (CR 702.9).
///
/// ## Implementation
///
/// - 3/2 <see cref="Creature"/> with <see cref="CardSubtype.Drake"/>,
///   mana cost {3}{U} (mana value 4, blue — CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat block-restriction path reads the marker directly —
///   same shape as <see cref="WindDrakeFactory"/>'s Flying.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Snapping Drake")]
public static class SnappingDrakeFactory
{
    public const string CardName = "Snapping Drake";
    public const string PrintedManaCost = "{3}{U}";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Snapping Drake — a {3}{U} 3/2 Creature — Drake with the
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
