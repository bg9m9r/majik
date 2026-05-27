using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nest Robber (Ixalan, {1}{R}).
///
/// Creature — Dinosaur 2/1. Oracle text:
///   "Haste"
///
/// A 2/1 red Dinosaur for two mana with Haste — Nest Robber is a
/// classic aggressive one-drop-range creature that can attack the turn
/// it enters the battlefield (CR 702.10). Pure vanilla Haste body,
/// no triggers or activated abilities.
///
/// ## Implementation
///
/// - 2/1 <see cref="Creature"/> with <see cref="CardSubtype.Dinosaur"/>,
///   mana cost {1}{R} (mana value 2, red — CR 202.3 / CR 105.1).
/// - <b>Haste (CR 702.10)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The summoning-sickness path reads the marker directly.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Nest Robber")]
public static class NestRobberFactory
{
    public const string CardName = "Nest Robber";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Constructs Nest Robber — a {1}{R} 2/1 Creature — Dinosaur with
    /// the Haste keyword marker.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Dinosaur });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — Haste marker. Summoning-sickness exemption enforced
        // by CombatRules / TurnRules.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        return card;
    }
}
