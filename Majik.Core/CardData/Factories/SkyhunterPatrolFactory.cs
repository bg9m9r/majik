using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Skyhunter Patrol (Mirrodin, {2}{W}{W}).
///
/// Creature — Cat Knight 2/3. Oracle text:
///   "Flying, first strike"
///
/// ## Implementation
///
/// - 2/3 <see cref="Creature"/> with <see cref="CardSubtype.Cat"/> and
///   <see cref="CardSubtype.Knight"/> subtypes, mana cost {2}{W}{W}
///   (mana value 4, white — CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/>("Flying")
///   marker. The combat block-restriction path reads the marker directly.
/// - <b>First Strike (CR 702.7)</b>: <see cref="KeywordAbility"/>
///   ("First Strike") marker. The combat damage assignment step reads
///   this marker to schedule Skyhunter Patrol in the first combat damage
///   step (CR 702.7b).
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
///
/// CR rule references: 205.3m (Cat / Knight subtypes), 702.7 (First
/// strike), 702.9 (Flying).
/// </summary>
[CardName("Skyhunter Patrol")]
public static class SkyhunterPatrolFactory
{
    public const string CardName = "Skyhunter Patrol";
    public const string PrintedManaCost = "{2}{W}{W}";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Constructs Skyhunter Patrol — a {2}{W}{W} 2/3 Creature — Cat Knight
    /// with Flying and First Strike keyword markers.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Cat, CardSubtype.Knight });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Block restrictions enforced by
        // CombatRules / CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.7 — First Strike marker. Schedules Skyhunter Patrol into
        // the first combat damage step before creatures without first
        // or double strike.
        card.AddAbility(new KeywordAbility("First Strike", card, owner));

        return card;
    }
}
