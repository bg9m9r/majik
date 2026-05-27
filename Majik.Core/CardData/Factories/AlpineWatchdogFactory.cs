using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Alpine Watchdog (Jumpstart 2022 / Core Set 2021,
/// {1}{W}).
///
/// Creature — Dog 2/2. Oracle text:
///   "Vigilance"
///
/// Alpine Watchdog is a straightforward white weenie — an efficiently-costed
/// 2/2 Dog that can attack each turn without leaving the controller
/// defenceless thanks to Vigilance.
///
/// ## Implementation
///
/// - 2/2 <see cref="Creature"/> with <see cref="CardSubtype.Dog"/>,
///   mana cost {1}{W} (mana value 2, white — CR 202.3 / CR 105.1).
/// - <b>Vigilance (CR 702.20)</b>: <see cref="KeywordAbility"/> marker;
///   CombatAbilities.HasVigilance / CombatValidator / Attacker.HasVigilance
///   consume it to suppress the attack-tap — same shape as
///   <see cref="SerraAngelFactory"/>'s Vigilance.
///
/// No triggers, no activated abilities — purely vanilla keyword creature.
/// </summary>
[CardName("Alpine Watchdog")]
public static class AlpineWatchdogFactory
{
    public const string CardName = "Alpine Watchdog";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Alpine Watchdog — a {1}{W} 2/2 Creature — Dog with
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
            subtypes: new[] { CardSubtype.Dog });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.20 — Vigilance marker. Attacking does not cause Alpine
        // Watchdog to tap; consumed by CombatAbilities.HasVigilance /
        // CombatValidator / Attacker.HasVigilance.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        return card;
    }
}
