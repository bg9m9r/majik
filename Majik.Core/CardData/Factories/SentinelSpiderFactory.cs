using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sentinel Spider (Magic 2013, {3}{G}{G}).
///
/// Creature — Spider 4/4. Oracle text:
///   "Vigilance, reach"
///
/// Sentinel Spider is a green defensive creature that can attack freely
/// without tapping (Vigilance) and block flying creatures (Reach). It
/// represents the quintessential green Spider — a ground-and-air wall
/// that can still apply offensive pressure.
///
/// ## Implementation
///
/// - 4/4 <see cref="Creature"/> with <see cref="CardSubtype.Spider"/>,
///   mana cost {3}{G}{G} (mana value 5, green — CR 202.3 / CR 105.1).
/// - <b>Vigilance (CR 702.20)</b>: <see cref="KeywordAbility"/> marker;
///   CombatAbilities.HasVigilance / CombatValidator / Attacker.HasVigilance
///   consume it to suppress the attack-tap — same shape as
///   <see cref="SerraAngelFactory"/>'s Vigilance.
/// - <b>Reach (CR 702.17)</b>: <see cref="KeywordAbility"/> marker;
///   consumed by CombatAbilities.HasReach to allow blocking of creatures
///   with Flying — same shape as <see cref="GenerousEntFactory"/>'s Reach.
///
/// No triggers, no activated abilities — purely vanilla keyword creature.
/// </summary>
[CardName("Sentinel Spider")]
public static class SentinelSpiderFactory
{
    public const string CardName = "Sentinel Spider";
    public const string PrintedManaCost = "{3}{G}{G}";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>
    /// Constructs Sentinel Spider — a {3}{G}{G} 4/4 Creature — Spider with
    /// the Vigilance and Reach keyword markers.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Spider });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.20 — Vigilance marker. Attacking does not cause Sentinel
        // Spider to tap; consumed by CombatAbilities.HasVigilance /
        // CombatValidator / Attacker.HasVigilance.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        // CR 702.17 — Reach marker. Sentinel Spider may block creatures
        // with Flying; consumed by CombatAbilities.HasReach.
        card.AddAbility(new KeywordAbility("Reach", card, owner));

        return card;
    }
}
