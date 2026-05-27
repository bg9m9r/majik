using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wild Griffin (Portal, {2}{W}).
///
/// Creature — Griffin 2/2. Oracle text:
///   "Flying"
///
/// A 2/2 evasive white flier for three mana — Wild Griffin is the
/// archetypal vanilla white creature with reach via the skies. It is
/// purely a vanilla flier: no triggers, no activated abilities, just
/// the printed Flying keyword (CR 702.9).
///
/// ## Implementation
///
/// - 2/2 <see cref="Creature"/> with <see cref="CardSubtype.Griffin"/>,
///   mana cost {2}{W} (mana value 3, white — CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat block-restriction path reads the marker directly —
///   same shape as <see cref="SuntailHawkFactory"/>'s Flying /
///   <see cref="WindDrakeFactory"/>'s Flying.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Wild Griffin")]
public static class WildGriffinFactory
{
    public const string CardName = "Wild Griffin";
    public const string PrintedManaCost = "{2}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Wild Griffin — a {2}{W} 2/2 Creature — Griffin with the
    /// Flying keyword marker.
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
