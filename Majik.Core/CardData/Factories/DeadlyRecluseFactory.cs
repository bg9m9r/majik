using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Deadly Recluse (Magic 2012, {1}{G}).
///
/// Creature — Spider 1/2. Oracle text:
///   "Reach, Deathtouch"
///
/// A 1/2 green Spider that can block flying creatures (Reach, CR 702.17)
/// and destroys any creature it deals damage to (Deathtouch, CR 702.2).
/// No triggers, no activated abilities — two printed keyword markers only.
///
/// ## Implementation
///
/// - {1}{G} 1/2 <see cref="Creature"/> with <see cref="CardSubtype.Spider"/>,
///   mana value 2 (CR 202.3 / CR 105.1).
/// - <b>Reach (CR 702.17)</b> attached as a <see cref="KeywordAbility"/>
///   marker — same shape as <see cref="GiantSpiderFactory"/>.
/// - <b>Deathtouch (CR 702.2)</b> attached as a <see cref="KeywordAbility"/>
///   marker — same shape as <see cref="PharikasChosenFactory"/>.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Deadly Recluse")]
public static class DeadlyRecluseFactory
{
    public const string CardName = "Deadly Recluse";
    public const string PrintedManaCost = "{1}{G}";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Deadly Recluse — a {1}{G} 1/2 Creature — Spider
    /// with Reach and Deathtouch keyword markers.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Spider });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.17 — Reach marker. Block restrictions enforced by
        // CombatRules / CombatAbilities (a creature with Reach can block
        // creatures with Flying).
        card.AddAbility(new KeywordAbility("Reach", card, owner));

        // CR 702.2 — Deathtouch marker. CombatAbilities.HasDeathtouch
        // consumes this for lethal-damage determination.
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));

        return card;
    }
}
