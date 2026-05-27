using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Giant Spider (Core sets, {3}{G}).
///
/// Creature — Spider 2/4. Oracle text:
///   "Reach" (CR 702.17)
///
/// A 2/4 green Spider — notable for its ability to block flying creatures
/// via Reach. Purely a vanilla Reach creature: no triggers, no activated
/// abilities, just the printed Reach keyword (CR 702.17).
///
/// ## Implementation
///
/// - 2/4 <see cref="Creature"/> with <see cref="CardSubtype.Spider"/>,
///   mana cost {3}{G} (mana value 4, green — CR 202.3 / CR 105.1).
/// - <b>Reach (CR 702.17)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat block-restriction path reads the marker directly —
///   same shape as <see cref="EnduranceFactory"/>'s Reach /
///   <see cref="KraulHarpoonerFactory"/>'s Reach.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Giant Spider")]
public static class GiantSpiderFactory
{
    public const string CardName = "Giant Spider";
    public const string PrintedManaCost = "{3}{G}";
    public const int Power = 2;
    public const int Toughness = 4;

    /// <summary>
    /// Constructs Giant Spider — a {3}{G} 2/4 Creature — Spider with the
    /// Reach keyword marker.
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

        // CR 702.17 — Reach marker. Block restrictions enforced by
        // CombatRules / CombatAbilities (a creature with Reach can block
        // creatures with Flying).
        card.AddAbility(new KeywordAbility("Reach", card, owner));

        return card;
    }
}
