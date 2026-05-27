using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Craw Wurm (Alpha / Modern reprints, {4}{G}{G}).
///
/// Creature — Wurm 6/4. Vanilla — no printed keywords, triggers, statics, or
/// activated abilities. A classic green beatstick: 6 power and 4 toughness for
/// four generic and two Green mana (mana value 6).
///
/// ## Implementation
///
/// - 6/4 <see cref="Creature"/> with <see cref="CardSubtype.Wurm"/>.
/// - Mana cost {4}{G}{G}; <see cref="ManaCost"/>'s parser derives Green from the
///   two coloured pips (CR 105.2). Mana value = 6.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Craw Wurm")]
public static class CrawWurmFactory
{
    public const string CardName = "Craw Wurm";
    public const string PrintedManaCost = "{4}{G}{G}";
    public const int Power = 6;
    public const int Toughness = 4;

    /// <summary>
    /// Constructs Craw Wurm — a vanilla {4}{G}{G} 6/4 Creature — Wurm.
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

        return card;
    }
}
