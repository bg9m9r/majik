using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ornithopter (Antiquities, {0}).
///
/// Artifact Creature — Thopter 0/2. Oracle text:
///   "Flying"
///
/// Cheap Affinity / Hardened Scales / Hammer-Time shell — fixed
/// evasive body that pairs with Cranial Plating, Colossus Hammer's
/// +10/+0 boost (Hammer strips flying — Ornithopter becomes a 10/2
/// ground creature), Arcbound Ravager modular fodder, and the 0-mana
/// enabler suite (Springleaf Drum, Mox Opal).
///
/// ## Implementation
///
/// - 0/2 <see cref="Creature"/> with <see cref="CardSubtype.Thopter"/>.
/// - <see cref="CardType.Artifact"/> additively stamped via
///   <see cref="Card.AddCardType"/>.
/// - Flying (CR 702.9) attached as a
///   <see cref="KeywordAbility"/> marker — combat helpers in
///   <see cref="Majik.Core.Combat.CombatAbilities"/> read it directly
///   (same shape as <see cref="ArcboundStingerFactory"/> /
///   <see cref="VaultSkirgeFactory"/>).
/// - Mana cost is the literal {0} string (same convention as
///   <see cref="MemniteFactory"/> / <see cref="MoxOpalFactory"/>).
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Ornithopter")]
public static class OrnithopterFactory
{
    public const string CardName = "Ornithopter";
    public const string PrintedManaCost = "{0}";
    public const int Power = 0;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Ornithopter — a {0} 0/2 Artifact Creature — Thopter
    /// with the Flying keyword marker.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Thopter });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag the
        // Artifact type so HasType lookups + colour identity see both
        // types.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker; combat reads this directly.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        return card;
    }
}
