using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hitchclaw Recluse (Magic 2014, {2}{G}).
///
/// Creature — Spider 1/4. Oracle text:
///   "Reach" (CR 702.17)
///
/// A 1/4 green Spider for three mana that can block flying creatures.
/// Hitchclaw Recluse is a pure keyword-marker creature: no triggers, no
/// activated abilities, just the printed Reach keyword.
///
/// ## Implementation
///
/// - 1/4 <see cref="Creature"/> with <see cref="CardSubtype.Spider"/>,
///   mana cost {2}{G} (mana value 3, green — CR 202.3 / CR 105.1).
/// - <b>Reach (CR 702.17)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat block-restriction path reads the marker directly —
///   same shape as <see cref="CanopySpiderFactory"/>'s Reach and
///   <see cref="KraulHarpoonerFactory"/>'s Reach.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Hitchclaw Recluse")]
public static class HitchclawRecluseFactory
{
    public const string CardName = "Hitchclaw Recluse";
    public const string PrintedManaCost = "{2}{G}";
    public const int Power = 1;
    public const int Toughness = 4;

    /// <summary>
    /// Constructs Hitchclaw Recluse — a {2}{G} 1/4 Creature — Spider with
    /// the Reach keyword marker (CR 702.17).
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

        // CR 702.17 — Reach marker. Lets Hitchclaw Recluse block creatures
        // with flying (CombatAbilities.CanBlockFlying reads this marker).
        card.AddAbility(new KeywordAbility("Reach", card, owner));

        return card;
    }
}
