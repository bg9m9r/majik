using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Razortooth Rats (Tenth Edition, {2}{B}).
///
/// Creature — Rat 2/1. Oracle text:
///   "Fear (This creature can't be blocked except by artifact creatures
///    and/or black creatures.)"
///
/// ## Implementation
///
/// - {2}{B} 2/1 <see cref="Creature"/> — Rat, mana value 3,
///   black (CR 202.3 / CR 105.1).
/// - <b>Fear (CR 702.36)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat system consumes Fear for block-restriction checks.
///
/// No triggers, no activated abilities — a single-keyword creature.
/// Single-arg <see cref="Create(Player)"/> is the canonical entry point.
/// </summary>
[CardName("Razortooth Rats")]
public static class RazortoothRatsFactory
{
    public const string CardName = "Razortooth Rats";
    public const string PrintedManaCost = "{2}{B}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Constructs Razortooth Rats — a {2}{B} 2/1 Creature — Rat
    /// with a Fear keyword marker.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Rat });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.36 — Fear keyword marker. Combat system checks this for
        // block restriction: only artifact creatures and black creatures
        // may block a creature with Fear.
        card.AddAbility(new KeywordAbility("Fear", card, owner));

        return card;
    }
}
