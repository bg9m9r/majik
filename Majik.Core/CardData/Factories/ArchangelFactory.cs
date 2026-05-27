using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Archangel (Visions, {5}{W}{W}).
///
/// Creature — Angel 5/5. Oracle text:
///   "Flying, vigilance"
///
/// Archangel is a white finisher — a 5/5 flier that can swing freely
/// without leaving the controller defenceless. Same keyword shape as
/// <see cref="SerraAngelFactory"/> at a higher cost and power/toughness.
///
/// ## Implementation
///
/// - 5/5 <see cref="Creature"/> with <see cref="CardSubtype.Angel"/>,
///   mana cost {5}{W}{W} (mana value 7, white — CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/> marker;
///   combat block-restriction path reads it directly.
/// - <b>Vigilance (CR 702.20)</b>: <see cref="KeywordAbility"/> marker;
///   CombatAbilities.HasVigilance / CombatValidator / Attacker.HasVigilance
///   consume it to suppress the attack-tap.
///
/// No triggers, no activated abilities — purely vanilla keyword creature.
/// </summary>
[CardName("Archangel")]
public static class ArchangelFactory
{
    public const string CardName = "Archangel";
    public const string PrintedManaCost = "{5}{W}{W}";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>
    /// Constructs Archangel — a {5}{W}{W} 5/5 Creature — Angel with
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

        // CR 702.20 — Vigilance marker. Attacking does not cause Archangel
        // to tap; consumed by CombatAbilities.HasVigilance /
        // CombatValidator / Attacker.HasVigilance.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        return card;
    }
}
