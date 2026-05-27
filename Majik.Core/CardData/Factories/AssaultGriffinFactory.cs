using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Assault Griffin (Magic 2012 / various Core Sets, {3}{W}).
///
/// Creature — Griffin 3/2. Oracle text:
///   "Flying"
///
/// A 3/2 evasive white flier for four mana — Assault Griffin is a solid
/// aggressive creature, able to attack over ground blockers with its Flying
/// evasion. Assault Griffin is purely a vanilla flier: no triggers, no
/// activated abilities, just the printed Flying keyword (CR 702.9).
///
/// ## Implementation
///
/// - 3/2 <see cref="Creature"/> with <see cref="CardSubtype.Griffin"/>,
///   mana cost {3}{W} (mana value 4, white — CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat block-restriction path reads the marker directly —
///   same shape as <see cref="AirElementalFactory"/>'s Flying.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Assault Griffin")]
public static class AssaultGriffinFactory
{
    public const string CardName = "Assault Griffin";
    public const string PrintedManaCost = "{3}{W}";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Assault Griffin — a {3}{W} 3/2 Creature — Griffin with
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
            subtypes: new[] { CardSubtype.Griffin });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Block restrictions enforced by
        // CombatRules / CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        return card;
    }
}
