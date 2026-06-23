using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Abbey Griffin (Innistrad, {3}{W}).
///
/// Creature — Griffin 2/2. Oracle text:
///   "Flying, vigilance"
///
/// A 2/2 evasive white flier for four mana — Abbey Griffin attacks over
/// ground blockers with its Flying evasion and stays untapped to defend
/// thanks to Vigilance. Same Griffin shape as
/// <see cref="AssaultGriffinFactory"/>, just one less point of power and the
/// added Vigilance keyword. Purely a vanilla keyword flier: no triggers, no
/// activated abilities, just the printed Flying + Vigilance keywords.
///
/// ## Implementation
///
/// - 2/2 <see cref="Creature"/> with <see cref="CardSubtype.Griffin"/>,
///   mana cost {3}{W} (mana value 4, white — CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat block-restriction path reads the marker directly.
/// - <b>Vigilance (CR 702.20)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat-abilities subsystem reads it via
///   CombatAbilities.HasVigilance to prevent tapping when declared as an
///   attacker.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Abbey Griffin")]
public static class AbbeyGriffinFactory
{
    public const string CardName = "Abbey Griffin";
    public const string PrintedManaCost = "{3}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Abbey Griffin — a {3}{W} 2/2 Creature — Griffin with the
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
            subtypes: new[] { CardSubtype.Griffin });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Block restrictions enforced by
        // CombatRules / CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.20 — Vigilance marker. Combat-abilities subsystem reads this
        // marker to prevent tapping when the creature is declared as an attacker.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        return card;
    }
}
