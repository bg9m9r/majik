using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Garruk's Companion (Magic 2011 / reprints, {G}{G}).
///
/// Creature — Beast 3/2. Oracle text:
///   "Trample"
///
/// ## Implementation
///
/// - 3/2 <see cref="Creature"/> with <see cref="CardSubtype.Beast"/>.
/// - Mana cost {G}{G}; two Green pips → Green colour identity (CR 105.2).
///   Mana value = 2.
/// - Trample (CR 702.19) wired as a <see cref="KeywordAbility"/> marker,
///   consumed by the combat-damage assignment logic when blocking/blocked.
/// - No other abilities — Garruk's Companion is keyword-only (Trample).
/// - Single-arg <see cref="Create(Player)"/> is the canonical entry point.
/// </summary>
[CardName("Garruk's Companion")]
public static class GarruksCompanionFactory
{
    public const string CardName = "Garruk's Companion";
    public const string PrintedManaCost = "{G}{G}";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Garruk's Companion — a {G}{G} 3/2 Creature — Beast with
    /// Trample (CR 702.19).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Beast });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.19 — Trample. Keyword marker consumed by combat-damage
        // assignment; excess combat damage is assigned to the defending
        // player / planeswalker once blockers are lethal.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        return card;
    }
}
