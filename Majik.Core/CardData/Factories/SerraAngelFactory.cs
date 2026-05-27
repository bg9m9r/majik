using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Serra Angel (Alpha, {3}{W}{W}).
///
/// Creature — Angel 4/4. Oracle text:
///   "Flying, vigilance"
///
/// Serra Angel is a cornerstone white creature — an aggressively-costed
/// 4/4 flier that can swing freely without leaving the controller
/// defenceless. It has been a Magic staple since Alpha and is the
/// quintessential white Angel.
///
/// ## Implementation
///
/// - 4/4 <see cref="Creature"/> with <see cref="CardSubtype.Angel"/>,
///   mana cost {3}{W}{W} (mana value 5, white — CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/> marker;
///   combat block-restriction path reads it directly — same shape as
///   <see cref="WindDrakeFactory"/>'s Flying.
/// - <b>Vigilance (CR 702.20)</b>: <see cref="KeywordAbility"/> marker;
///   CombatAbilities.HasVigilance / CombatValidator / Attacker.HasVigilance
///   consume it to suppress the attack-tap — same shape as
///   <see cref="SunTitanFactory"/>'s Vigilance.
///
/// No triggers, no activated abilities — purely vanilla keyword creature.
/// </summary>
[CardName("Serra Angel")]
public static class SerraAngelFactory
{
    public const string CardName = "Serra Angel";
    public const string PrintedManaCost = "{3}{W}{W}";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>
    /// Constructs Serra Angel — a {3}{W}{W} 4/4 Creature — Angel with
    /// the Flying and Vigilance keyword markers.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Angel });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Block restrictions enforced by
        // CombatRules / CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.20 — Vigilance marker. Attacking does not cause Serra
        // Angel to tap; consumed by CombatAbilities.HasVigilance /
        // CombatValidator / Attacker.HasVigilance.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        return card;
    }
}
