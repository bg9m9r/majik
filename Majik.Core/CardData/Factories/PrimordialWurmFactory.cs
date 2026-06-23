using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Primordial Wurm (Core Set 2019 / reprints,
/// {4}{G}{G}).
///
/// Creature — Wurm 7/6. Oracle text (verified Scryfall 2026-06-23): empty —
/// vanilla. No printed keywords, triggers, statics, or activated abilities. A
/// big green beatstick: 7 power and 6 toughness for four generic and two Green
/// mana (mana value 6).
///
/// ## Implementation
///
/// - 7/6 <see cref="Creature"/> with <see cref="CardSubtype.Wurm"/>.
/// - Mana cost {4}{G}{G}; <see cref="ManaCost"/>'s parser derives Green from the
///   two coloured pips (CR 105.2). Mana value = 6.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point. Same vanilla posture as
///   <see cref="CrawWurmFactory"/>, just larger P/T.
/// </summary>
[CardName("Primordial Wurm")]
public static class PrimordialWurmFactory
{
    public const string CardName = "Primordial Wurm";
    public const string PrintedManaCost = "{4}{G}{G}";
    public const int Power = 7;
    public const int Toughness = 6;

    /// <summary>
    /// Constructs Primordial Wurm — a vanilla {4}{G}{G} 7/6 Creature — Wurm.
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
