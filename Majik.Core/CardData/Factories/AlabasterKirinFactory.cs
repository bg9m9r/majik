using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Alabaster Kirin (Dragons of Tarkir, {3}{W}).
///
/// Creature — Kirin 2/3. Oracle text (verified against Scryfall 2026-06-23):
///   "Flying, vigilance"
///
/// A {3}{W} 2/3 evasive white flier that can attack without tapping —
/// Alabaster Kirin is a purely french-vanilla keyword creature: no triggers,
/// no activated abilities, just the printed Flying + Vigilance markers. Same
/// shape as <see cref="SerraAngelFactory"/> (Flying + Vigilance), one mana
/// value cheaper with a smaller body.
///
/// ## Implementation
///
/// - 2/3 <see cref="Creature"/> with <see cref="CardSubtype.Kirin"/>,
///   mana cost {3}{W} (mana value 4, white — CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/> marker; the
///   combat block-restriction path reads it directly — same shape as
///   <see cref="AssaultGriffinFactory"/>'s Flying.
/// - <b>Vigilance (CR 702.20)</b>: <see cref="KeywordAbility"/> marker;
///   CombatAbilities.HasVigilance / CombatValidator / Attacker.HasVigilance
///   consume it to suppress the attack-tap — same shape as
///   <see cref="SerraAngelFactory"/>'s Vigilance.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Alabaster Kirin")]
public static class AlabasterKirinFactory
{
    public const string CardName = "Alabaster Kirin";
    public const string PrintedManaCost = "{3}{W}";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Constructs Alabaster Kirin — a {3}{W} 2/3 Creature — Kirin with the
    /// Flying and Vigilance keyword markers.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Kirin });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Block restrictions enforced by
        // CombatRules / CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.20 — Vigilance marker. Attacking does not cause Alabaster
        // Kirin to tap; consumed by CombatAbilities.HasVigilance /
        // CombatValidator / Attacker.HasVigilance.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        return card;
    }
}
