using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vampire Nighthawk (Zendikar, {1}{B}{B}).
///
/// Creature — Vampire Shaman 2/3. Oracle text:
///   "Flying, deathtouch, lifelink"
///
/// ## Implementation
///
/// - {1}{B}{B} 2/3 <see cref="Creature"/> — Vampire Shaman, mana value 3,
///   black (CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b>, <b>Deathtouch (CR 702.2)</b>, and
///   <b>Lifelink (CR 702.15)</b> attached as <see cref="KeywordAbility"/>
///   markers. <see cref="Majik.Core.Combat.CombatAbilities"/> consumes
///   Deathtouch (lethal damage) and Lifelink (life gain); the block
///   restriction path reads Flying.
///
/// No triggers, no activated abilities — a clean keyword-only creature.
/// Single-arg <see cref="Create(Player)"/> is the canonical entry point.
/// </summary>
[CardName("Vampire Nighthawk")]
public static class VampireNighthawkFactory
{
    /// <summary>
    /// Constructs Vampire Nighthawk — a {1}{B}{B} 2/3 Creature — Vampire
    /// Shaman with Flying, Deathtouch, and Lifelink keyword markers.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Vampire Nighthawk",
            manaCost: "{1}{B}{B}",
            power: 2,
            toughness: 3,
            subtypes: new[] { CardSubtype.Vampire, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Block restrictions enforced by
        // CombatRules / CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.2 — Deathtouch marker. CombatAbilities.HasDeathtouch
        // consumes this for lethal-damage determination.
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));

        // CR 702.15 — Lifelink marker. CombatAbilities.HasLifelink
        // consumes this for life-gain on combat damage.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        return card;
    }
}
