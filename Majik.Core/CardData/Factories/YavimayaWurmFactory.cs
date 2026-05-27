using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Yavimaya Wurm (Urza's Legacy / reprints, {4}{G}{G}).
///
/// Creature — Wurm 6/4. Oracle text:
///   "Trample"
///
/// ## Implementation
///
/// - 6/4 <see cref="Creature"/> with <see cref="CardSubtype.Wurm"/>.
/// - Mana cost {4}{G}{G}; four generic + two Green pips → Green colour identity
///   (CR 105.2). Mana value = 6.
/// - Trample (CR 702.19) wired as a <see cref="KeywordAbility"/> marker,
///   consumed by the combat-damage assignment logic when blocking/blocked.
/// - No other abilities — Yavimaya Wurm is keyword-only (Trample).
/// - Single-arg <see cref="Create(Player)"/> is the canonical entry point.
/// </summary>
[CardName("Yavimaya Wurm")]
public static class YavimayaWurmFactory
{
    public const string CardName = "Yavimaya Wurm";
    public const string PrintedManaCost = "{4}{G}{G}";
    public const int Power = 6;
    public const int Toughness = 4;

    /// <summary>
    /// Constructs Yavimaya Wurm — a {4}{G}{G} 6/4 Creature — Wurm with
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
            subtypes: new[] { CardSubtype.Wurm });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.19 — Trample. Keyword marker consumed by combat-damage
        // assignment; excess combat damage is assigned to the defending
        // player / planeswalker once blockers are lethal.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        return card;
    }
}
