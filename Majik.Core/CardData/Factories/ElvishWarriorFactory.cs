using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Elvish Warrior (Onslaught, {G}{G}).
///
/// Creature — Elf Warrior 2/3. Vanilla — no printed keywords, triggers,
/// statics, or activated abilities.
///
/// ## Implementation
///
/// - 2/3 <see cref="Creature"/> with subtypes
///   <see cref="CardSubtype.Elf"/> + <see cref="CardSubtype.Warrior"/>.
/// - Mana cost {G}{G} (two green pips, converted mana cost 2).
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Elvish Warrior")]
public static class ElvishWarriorFactory
{
    public const string CardName = "Elvish Warrior";
    public const string PrintedManaCost = "{G}{G}";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Constructs Elvish Warrior — a vanilla {G}{G} 2/3 Creature —
    /// Elf Warrior.
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
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
