using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mahamoti Djinn (Alpha/Beta/many reprints, {4}{U}{U}).
///
/// Creature — Djinn 5/6. Oracle text:
///   "Flying"
///
/// Mahamoti Djinn is a classic Alpha rare: a large vanilla blue flier whose
/// 5/6 body dominated the early game when it was first printed. It has no
/// triggered or activated abilities — just the Flying keyword marker.
///
/// ## Implementation
///
/// - 5/6 <see cref="Creature"/> with <see cref="CardSubtype.Djinn"/>,
///   mana cost {4}{U}{U} (mana value 6, blue — CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat block-restriction path reads the marker directly —
///   same shape as <see cref="StormCrowFactory"/>'s and
///   <see cref="SerraAngelFactory"/>'s Flying.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Mahamoti Djinn")]
public static class MahamotiDjinnFactory
{
    public const string CardName = "Mahamoti Djinn";
    public const string PrintedManaCost = "{4}{U}{U}";
    public const int Power = 5;
    public const int Toughness = 6;

    /// <summary>
    /// Constructs Mahamoti Djinn — a {4}{U}{U} 5/6 Creature — Djinn with
    /// the Flying keyword marker.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Djinn });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Block restrictions enforced by
        // CombatRules / CombatAbilities (same wire-up shape as
        // StormCrowFactory / SerraAngelFactory).
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        return card;
    }
}
