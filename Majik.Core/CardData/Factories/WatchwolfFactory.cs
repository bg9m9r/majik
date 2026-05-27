using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Watchwolf (Ravnica: City of Guilds, {G}{W}).
///
/// Creature — Wolf 3/3. Vanilla — no printed keywords, triggers, statics,
/// or activated abilities. A classic Selesnya bear: 3 power and 3 toughness
/// for two mana across Green and White.
///
/// ## Implementation
///
/// - 3/3 <see cref="Creature"/> with <see cref="CardSubtype.Wolf"/>.
/// - Mana cost {G}{W}; <see cref="ManaCost"/>'s parser derives Green and
///   White from the two coloured pips (CR 105.2).
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Watchwolf")]
public static class WatchwolfFactory
{
    public const string CardName = "Watchwolf";
    public const string PrintedManaCost = "{G}{W}";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>
    /// Constructs Watchwolf — a vanilla {G}{W} 3/3 Creature — Wolf.
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
            subtypes: new[] { CardSubtype.Wolf });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
