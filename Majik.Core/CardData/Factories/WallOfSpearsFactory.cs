using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wall of Spears (Alpha / Beta / many reprints, {3}).
///
/// Artifact Creature — Wall 2/3. Oracle text:
///   "Defender.
///    First strike."
///
/// ## Implementation
///
/// - 2/3 <see cref="Creature"/> with <see cref="CardSubtype.Wall"/>.
/// - <see cref="CardType.Artifact"/> additively stamped via
///   <see cref="Card.AddCardType"/> (CR 301.1 / 302.1 — Artifact Creature:
///   both types must be set so HasType lookups and colour-identity rules
///   see Artifact AND Creature; mirrors <see cref="MemniteFactory"/> /
///   <see cref="OrnithopterFactory"/>).
/// - Mana cost is the literal {3} string — generic-only, so the card is
///   colourless (CR 202.2). Mana value 3 (CR 202.3).
/// - <b>Defender (CR 702.3)</b>: <see cref="KeywordAbility"/> marker
///   "Defender" so <see cref="Majik.Core.Combat.CombatAbilities.HasDefender"/>
///   surfaces it for block-legality and the combat system treats the card
///   as a blocker only.
/// - <b>First strike (CR 702.7)</b>: <see cref="KeywordAbility"/> marker
///   "First strike" read by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/>
///   for the first-strike damage step (mirrors
///   <see cref="PhyrexianCrusaderFactory"/> / <see cref="ThaliaGuardianOfThrabenFactory"/>).
///
/// No activated abilities, triggered abilities, or service wiring — single-arg
/// <see cref="Create(Player)"/> is the canonical entry point.
/// </summary>
[CardName("Wall of Spears")]
public static class WallOfSpearsFactory
{
    public const string CardName = "Wall of Spears";
    public const string PrintedManaCost = "{3}";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Constructs Wall of Spears — a {3} 2/3 Artifact Creature — Wall with
    /// Defender and First strike keyword markers.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Wall });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag the
        // Artifact type so HasType lookups + colour identity see both
        // types (mirrors Memnite / Ornithopter).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.3 — Defender. Marker; CombatAbilities.HasDefender reads
        // this for block-legality (Wall of Spears may only block, not attack).
        card.AddAbility(new KeywordAbility("Defender", card, owner));

        // CR 702.7 — First strike. Marker; the combat first-strike damage
        // step reads this via CombatAbilities.HasFirstStrike.
        card.AddAbility(new KeywordAbility("First strike", card, owner));

        return card;
    }
}
