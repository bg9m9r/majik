using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Typhoid Rats (Innistrad, {B}).
///
/// Creature — Rat 1/1. Oracle text:
///   "Deathtouch"
///
/// ## Implementation
///
/// - {B} 1/1 <see cref="Creature"/> — Rat, mana value 1,
///   black (CR 202.3 / CR 105.1).
/// - <b>Deathtouch (CR 702.2)</b> attached as a <see cref="KeywordAbility"/>
///   marker. <see cref="Majik.Core.Combat.CombatAbilities"/> consumes
///   Deathtouch for lethal-damage determination.
///
/// No triggers, no activated abilities — a single-keyword creature.
/// Single-arg <see cref="Create(Player)"/> is the canonical entry point.
/// </summary>
[CardName("Typhoid Rats")]
public static class TyphoidRatsFactory
{
    public const string CardName = "Typhoid Rats";
    public const string PrintedManaCost = "{B}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Constructs Typhoid Rats — a {B} 1/1 Creature — Rat
    /// with a Deathtouch keyword marker.
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

        // CR 702.2 — Deathtouch marker. CombatAbilities.HasDeathtouch
        // consumes this for lethal-damage determination.
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));

        return card;
    }
}
