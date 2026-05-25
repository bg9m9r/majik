using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phyrexian Walker (Homelands, {0}).
///
/// Artifact Creature — Construct 0/3. Vanilla — no printed keywords,
/// triggers, statics, or activated abilities. Defensive 0-cost
/// Affinity / Hardened Scales fodder — pairs with Cranial Plating,
/// Arcbound Ravager (sac for a +1/+1 counter), and Modular trigger
/// recipients.
///
/// ## Implementation
///
/// - 0/3 <see cref="Creature"/> with <see cref="CardSubtype.Construct"/>.
/// - <see cref="CardType.Artifact"/> additively stamped via
///   <see cref="Card.AddCardType"/>.
/// - Mana cost is the literal {0} string (same convention as
///   <see cref="MemniteFactory"/>).
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Phyrexian Walker")]
public static class PhyrexianWalkerFactory
{
    public const string CardName = "Phyrexian Walker";
    public const string PrintedManaCost = "{0}";
    public const int Power = 0;
    public const int Toughness = 3;

    /// <summary>
    /// Constructs Phyrexian Walker — a vanilla {0} 0/3 Artifact Creature
    /// — Construct.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Construct });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag the
        // Artifact type so HasType lookups + colour identity see both
        // types.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
