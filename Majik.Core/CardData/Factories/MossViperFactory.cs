using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Moss Viper (Innistrad: Midnight Hunt, {G}).
///
/// Creature — Snake 1/1. Oracle text:
///   "Deathtouch"
///
/// A {G} 1/1 green Snake with Deathtouch — any amount of damage Moss Viper
/// deals is enough to destroy a creature (CR 702.2). A staple one-drop threat
/// in green, it demands a block or an answer immediately.
///
/// ## Implementation
///
/// - {G} 1/1 <see cref="Creature"/> with <see cref="CardSubtype.Snake"/>,
///   mana value 1, green (CR 202.3 / CR 105.1).
/// - <b>Deathtouch (CR 702.2)</b> attached as a <see cref="KeywordAbility"/>
///   marker — same shape as <see cref="DeadlyRecluseFactory"/>'s Deathtouch.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Moss Viper")]
public static class MossViperFactory
{
    public const string CardName = "Moss Viper";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Constructs Moss Viper — a {G} 1/1 Creature — Snake with the
    /// Deathtouch keyword marker.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Snake });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.2 — Deathtouch marker. CombatAbilities.HasDeathtouch
        // consumes this for lethal-damage determination.
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));

        return card;
    }
}
