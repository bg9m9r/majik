using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boggart Ram-Gang (Shadowmoor, {R/G}{R/G}{R/G}).
///
/// Creature — Goblin Warrior 3/3. Oracle text (Scryfall, verified 2026-06-02):
///   "Haste
///    Wither (This deals damage to creatures in the form of -1/-1 counters.)"
///
/// An aggressive three-hybrid-mana beater: hasty (can attack the turn it
/// enters) and its combat damage permanently shrinks blockers via -1/-1
/// counters rather than wearing off at cleanup.
///
/// ## Implemented (v1)
/// - 3/3 Creature — Goblin Warrior, mana cost {R/G}{R/G}{R/G} (hybrid red/green
///   stored as the printed string; mana value 3). Owner / controller stamped.
/// - <b>Haste (CR 702.10)</b>: <see cref="KeywordAbility"/> marker —
///   <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/> reads it so the
///   creature can attack / tap the turn it enters. Same wiring shape as
///   <see cref="BoggartBruteFactory"/>'s Menace marker.
/// - <b>Wither (CR 702.90a)</b>: <see cref="KeywordAbility"/> marker. At every
///   creature-damage application site (combat — <c>CombatFlow</c>; noncombat
///   fight — <c>Fx.Fight</c>) <see cref="Majik.Core.Combat.CombatAbilities.DealsCreatureDamageAsMinusCounters"/>
///   reads the marker and the damage is dealt to creatures as that many
///   <see cref="Majik.Core.Counters.CounterType.MinusOneMinusOne"/> counters
///   instead of marked damage (CR 702.90b). Damage to players / planeswalkers
///   is unaffected (normal). The Layer 7c P/T mod + CR 704.5g state-based
///   action handle lethal-via-0-toughness.
/// </summary>
[CardName("Boggart Ram-Gang")]
public static class BoggartRamGangFactory
{
    public const string CardName = "Boggart Ram-Gang";
    public const string PrintedManaCost = "{R/G}{R/G}{R/G}";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Boggart Ram-Gang owned and controlled by
    /// <paramref name="owner"/>. The Haste and Wither keyword markers are
    /// attached to the card; no additional services are required (both
    /// keywords are read off the marker at combat- / fight-damage time).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — Haste marker. Read by CombatAbilities.HasHaste so the
        // creature can attack / activate {T} abilities the turn it enters.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // CR 702.90a — Wither marker. Read by
        // CombatAbilities.DealsCreatureDamageAsMinusCounters at every
        // creature-damage site so combat / fight damage to creatures lands as
        // -1/-1 counters (CR 702.90b).
        card.AddAbility(new KeywordAbility("Wither", card, owner));

        return card;
    }
}
