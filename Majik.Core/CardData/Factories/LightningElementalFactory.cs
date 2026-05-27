using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lightning Elemental (various Core Sets, {3}{R}).
///
/// Creature — Elemental 4/1. Oracle text:
///   "Haste"
///
/// Lightning Elemental is a red aggro creature that trades toughness for speed —
/// a 4/1 body with Haste can attack immediately on the turn it enters, making it
/// a high-pressure threat in red tempo and burn strategies. No other abilities;
/// purely a vanilla Haste beater.
///
/// ## Implementation
///
/// - 4/1 <see cref="Creature"/> with <see cref="CardSubtype.Elemental"/>,
///   mana cost {3}{R} (mana value 4, red — CR 202.3 / CR 105.1).
/// - <b>Haste (CR 702.10)</b> attached as a <see cref="KeywordAbility"/>
///   marker. <c>CombatAbilities.HasHaste</c> reads the marker to bypass
///   the summoning-sickness check at CR 302.1 — same shape as Strangleroot
///   Geist, Earthshaker Khenra, and Goblin Chieftain.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Lightning Elemental")]
public static class LightningElementalFactory
{
    public const string CardName = "Lightning Elemental";
    public const string PrintedManaCost = "{3}{R}";
    public const int Power = 4;
    public const int Toughness = 1;

    /// <summary>
    /// Constructs Lightning Elemental — a {3}{R} 4/1 Creature — Elemental with
    /// the Haste keyword marker.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elemental });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — Haste. KeywordAbility marker; CombatAbilities.HasHaste
        // reads it so the creature can attack the turn it enters (bypasses
        // summoning sickness at CR 302.1). Same shape as Strangleroot Geist /
        // Earthshaker Khenra / Goblin Chieftain.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        return card;
    }
}
