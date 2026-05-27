using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Standing Troops ({2}{W} Creature — Human Soldier 1/4).
///
/// Oracle text:
///   "Vigilance"
///
/// Standing Troops is a defensive white creature with high toughness and
/// Vigilance, letting it attack without tapping — useful in both aggro and
/// control strategies.
///
/// ## Implementation
///
/// - 1/4 <see cref="Creature"/> with <see cref="CardSubtype.Human"/> and
///   <see cref="CardSubtype.Soldier"/>, mana cost {2}{W} (mana value 3, white
///   — CR 202.3 / CR 105.1).
/// - <b>Vigilance (CR 702.20)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat-abilities subsystem reads the marker via
///   CombatAbilities.HasVigilance to prevent tapping when declared as an
///   attacker.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Standing Troops")]
public static class StandingTroopsFactory
{
    public const string CardName = "Standing Troops";
    public const string PrintedManaCost = "{2}{W}";
    public const int Power = 1;
    public const int Toughness = 4;

    /// <summary>
    /// Constructs Standing Troops — a {2}{W} 1/4 Creature — Human Soldier with
    /// the Vigilance keyword marker.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.20 — Vigilance marker. Combat-abilities subsystem reads this
        // marker to prevent tapping when the creature is declared as an attacker.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        return card;
    }
}
