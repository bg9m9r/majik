using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Razorfoot Griffin (Portal Second Age, {3}{W}).
///
/// Creature — Griffin 2/2. Oracle text:
///   "Flying"
///   "First strike"
///
/// A 2/2 evasive white flier for four mana with both Flying and First
/// Strike — Razorfoot Griffin is a vanilla double-keyword creature
/// combining aerial evasion with the first-strike combat advantage.
///
/// ## Implementation
///
/// - 2/2 <see cref="Creature"/> with <see cref="CardSubtype.Griffin"/>,
///   mana cost {3}{W} (mana value 4, white — CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker. Block restrictions enforced by CombatRules / CombatAbilities.
/// - <b>First Strike (CR 702.7)</b> attached as a <see cref="KeywordAbility"/>
///   marker. Combat damage step handled by the engine's first-strike
///   combat damage ordering.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Razorfoot Griffin")]
public static class RazorfootGriffinFactory
{
    public const string CardName = "Razorfoot Griffin";
    public const string PrintedManaCost = "{3}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Razorfoot Griffin — a {3}{W} 2/2 Creature — Griffin with
    /// Flying and First Strike keyword markers.
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

        // CR 702.7 — First Strike marker. Combat damage step ordering
        // handled by the engine's first-strike combat damage logic.
        card.AddAbility(new KeywordAbility("First Strike", card, owner));

        return card;
    }
}
