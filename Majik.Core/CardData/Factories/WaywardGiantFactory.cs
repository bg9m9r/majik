using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wayward Giant (Eldritch Moon, {4}{R}).
///
/// Creature — Giant 4/5. Oracle text:
///   "Menace (This creature can't be blocked except by two or more
///    creatures.)"
///
/// A 4/5 red Giant for five mana with Menace — Wayward Giant is a
/// straightforward aggressive threat that is difficult to block. Its
/// combination of a large body and evasion via Menace makes it effective
/// in red midrange and aggressive strategies.
///
/// ## Implementation
///
/// - 4/5 <see cref="Creature"/> with <see cref="CardSubtype.Giant"/>,
///   mana cost {4}{R} (mana value 5, red — CR 202.3 / CR 105.1).
/// - <b>Menace (CR 702.110)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat block-restriction path reads the marker directly —
///   same wiring shape as <see cref="BoggartBruteFactory"/> /
///   <see cref="InsolentNeonateFactory"/> / <see cref="GriefFactory"/>.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Wayward Giant")]
public static class WaywardGiantFactory
{
    public const string CardName = "Wayward Giant";
    public const string PrintedManaCost = "{4}{R}";
    public const int Power = 4;
    public const int Toughness = 5;

    /// <summary>
    /// Constructs Wayward Giant — a {4}{R} 4/5 Creature — Giant with the
    /// Menace keyword marker.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Giant });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.110 — Menace keyword marker. Consumed by
        // CombatAbilities.HasMenace at block-declaration time.
        card.AddAbility(new KeywordAbility("Menace", card, owner));

        return card;
    }
}
