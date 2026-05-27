using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Youthful Knight ({1}{W} Creature — Human Knight 2/1).
///
/// Oracle text:
///   "First strike"
///
/// Youthful Knight is a two-mana white creature with two subtypes (Human and
/// Knight) and the First strike keyword. Its low cost and aggressive stats
/// (2/1) make it a classic early-game threat in White Weenie and Knight
/// tribal strategies.
///
/// ## Implementation
///
/// - 2/1 <see cref="Creature"/> with <see cref="CardSubtype.Human"/> and
///   <see cref="CardSubtype.Knight"/>, mana cost {1}{W} (mana value 2, white
///   — CR 202.3 / CR 105.1).
/// - <b>First strike (CR 702.7)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat-damage step reads the marker to assign first-strike
///   damage before regular damage.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Youthful Knight")]
public static class YouthfulKnightFactory
{
    public const string CardName = "Youthful Knight";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Constructs Youthful Knight — a {1}{W} 2/1 Creature — Human Knight with
    /// the First strike keyword marker.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Knight });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.7 — First strike marker. Combat-damage step enforces
        // first-strike damage assignment before regular combat damage.
        card.AddAbility(new KeywordAbility("First strike", card, owner));

        return card;
    }
}
