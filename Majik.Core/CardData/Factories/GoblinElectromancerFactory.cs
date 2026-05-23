using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Electromancer (Return to Ravnica / Modern
/// Horizons reprint family — Creature {U}{R}).
///
/// Oracle text:
///   "Instant and sorcery spells you cast cost {1} less to cast."
///
/// ## Implemented (v1)
/// - Creature {U}{R} 2/2 — Goblin Wizard, owner/controller wired.
/// - <b>Spell-cost reduction rider (CR 117.7 / CR 601.2f)</b> wired via
///   <see cref="SpellCostReductionAbility"/>. The predicate matches
///   instant or sorcery spells (via <see cref="ICard.HasType"/>); the
///   reduction is a flat 1 generic per cast. Scoped to the caster's
///   battlefield by <see cref="CostReduction.GetEffectiveCost"/> — only
///   the controller of this Goblin Electromancer benefits ("spells you
///   cast"). Coloured pips are untouched (CR 117.7c); floor-at-zero is
///   enforced inside the cost-calc helper so a {R} instant pays {R} when
///   this is the only reducer in play.
///
/// Multiple copies stack: two Goblin Electromancers reduce the cost of
/// each instant/sorcery by {2}. Symmetric across instants and sorceries
/// — creature / artifact / planeswalker / enchantment / land spells are
/// untouched.
/// </summary>
public static class GoblinElectromancerFactory
{
    public const string CardName = "Goblin Electromancer";
    public const string PrintedManaCost = "{U}{R}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Goblin Electromancer with the spell-cost reduction rider
    /// attached as static metadata. Cost-calc scan is handled by
    /// <see cref="CostReduction.GetEffectiveCost"/> at cast time.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 117.7 — "Instant and sorcery spells you cast cost {1} less to
        // cast." Predicate gates on the spell's card type; reduction is a
        // flat 1 generic. CostReduction.GetEffectiveCost scans only the
        // caster's battlefield for this ability shape, so the "you cast"
        // scope is enforced by the cost-calc helper.
        card.AddAbility(new SpellCostReductionAbility(
            predicate: c => c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery),
            reduction: (_, _) => 1,
            description: "Instant and sorcery spells you cast cost {1} less to cast."));

        return card;
    }
}
