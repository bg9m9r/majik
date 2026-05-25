using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Memnite (Scars of Mirrodin, {0}).
///
/// Artifact Creature — Construct 1/1. Vanilla — no printed keywords,
/// triggers, statics, or activated abilities. Cheap Affinity / Hardened
/// Scales / Hammer-Time shell — pairs with Cranial Plating, Arcbound
/// Ravager, Modular, and the 0-mana enabler suite (Springleaf Drum,
/// Mox Opal).
///
/// ## Implementation
///
/// - 1/1 <see cref="Creature"/> with <see cref="CardSubtype.Construct"/>.
/// - <see cref="CardType.Artifact"/> additively stamped via
///   <see cref="Card.AddCardType"/> so HasType lookups + colour identity
///   see both Artifact + Creature (mirrors
///   <see cref="ArcboundWorkerFactory"/> / <see cref="FrogmiteFactory"/>).
/// - Mana cost is the literal {0} string; <see cref="ManaCost"/>'s parser
///   reads this as zero generic + zero coloured (same convention as
///   <see cref="MoxOpalFactory"/> / <see cref="LotusPetalFactory"/> /
///   <see cref="ManaCryptFactory"/>).
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Memnite")]
public static class MemniteFactory
{
    public const string CardName = "Memnite";
    public const string PrintedManaCost = "{0}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Constructs Memnite — a vanilla {0} 1/1 Artifact Creature — Construct.
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
        // types (mirrors Arcbound Worker / Frogmite).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
