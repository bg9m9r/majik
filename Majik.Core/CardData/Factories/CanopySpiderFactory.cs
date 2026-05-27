using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Canopy Spider (Classic Sixth Edition, {1}{G}).
///
/// Creature — Spider 1/3. Oracle text:
///   "Reach" (CR 702.17)
///
/// A 1/3 green Spider for two mana that can block flying creatures.
/// Canopy Spider is a pure keyword-marker creature: no triggers, no
/// activated abilities, just the printed Reach keyword.
///
/// ## Implementation
///
/// - 1/3 <see cref="Creature"/> with <see cref="CardSubtype.Spider"/>,
///   mana cost {1}{G} (mana value 2, green — CR 202.3 / CR 105.1).
/// - <b>Reach (CR 702.17)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat block-restriction path reads the marker directly —
///   same shape as <see cref="KraulHarpoonerFactory"/>'s Reach /
///   <see cref="WorldBreakerFactory"/>'s Reach.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Canopy Spider")]
public static class CanopySpiderFactory
{
    public const string CardName = "Canopy Spider";
    public const string PrintedManaCost = "{1}{G}";
    public const int Power = 1;
    public const int Toughness = 3;

    /// <summary>
    /// Constructs Canopy Spider — a {1}{G} 1/3 Creature — Spider with the
    /// Reach keyword marker (CR 702.17).
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

        // CR 702.17 — Reach marker. Lets Canopy Spider block creatures
        // with flying (CombatAbilities.CanBlockFlying reads this marker).
        card.AddAbility(new KeywordAbility("Reach", card, owner));

        return card;
    }
}
