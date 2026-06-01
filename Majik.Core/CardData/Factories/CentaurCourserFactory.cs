using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Centaur Courser (Magic 2010 / Modern reprints, {2}{G}).
///
/// Creature — Centaur Warrior 3/3. Vanilla — no printed keywords, triggers,
/// statics, or activated abilities. A plain green body: 3 power and 3
/// toughness for two generic mana and one Green mana (mana value 3).
///
/// Previously this card was an inline fallback in
/// <see cref="NamedCardFactory"/> (a bare <c>new Creature(name, "2G", 3, 3)</c>
/// with no subtypes). Promoting it to a real <c>[CardName]</c> factory gives
/// it its proper Centaur + Warrior subtypes and removes it from
/// <see cref="ImplementedCardNames.InlineFallbackNames"/> so it reports as
/// factory-backed (closing v1 deferral #9).
///
/// ## Implementation
///
/// - 3/3 <see cref="Creature"/> with <see cref="CardSubtype.Centaur"/> and
///   <see cref="CardSubtype.Warrior"/>.
/// - Mana cost {2}{G}; <see cref="ManaCost"/>'s parser derives Green from the
///   single coloured pip (CR 105.2). Mana value = 3.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Centaur Courser")]
public static class CentaurCourserFactory
{
    public const string CardName = "Centaur Courser";
    public const string PrintedManaCost = "{2}{G}";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>
    /// Constructs Centaur Courser — a vanilla {2}{G} 3/3 Creature — Centaur Warrior.
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
            subtypes: new[] { CardSubtype.Centaur, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
