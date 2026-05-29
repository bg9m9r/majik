using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ruby Medallion (Tempest, {2}).
///
/// Artifact. Oracle text:
///   "Red spells you cast cost {1} less to cast."
///
/// ## Implemented (v1)
/// - Artifact, {2}, owner/controller wired.
/// - <b>Spell-cost reduction rider (CR 117.7 / CR 601.2f)</b> wired via
///   <see cref="SpellCostReductionAbility"/>. The predicate matches any
///   spell whose colours include red — unlike Goblin Anarchomancer
///   (instant/sorcery only), the Medallions discount EVERY spell of their
///   colour, so there is no card-type gate.
/// - Reduction is a flat {1} generic; coloured pips are untouched
///   (CR 117.7c) and the bucket floors at zero inside
///   <see cref="CostReduction.GetEffectiveCost"/>.
/// - "Spells you cast" scope is enforced by
///   <see cref="CostReduction.GetEffectiveCost"/>: only reducers on the
///   caster's battlefield contribute. Multiple Medallions stack linearly
///   (each contributes its own {1}).
///
/// ## Colour determination
/// Colour comes from <see cref="CardColors.GetColors"/> which reads the
/// card's printed mana cost / colour indicator (CR 105.2 / 202.2). Hybrid
/// pips contribute both listed colours; Phyrexian pips contribute the
/// named colour.
///
/// ## Schema note
/// This is a hand-coded C# factory rather than a JSON
/// <c>CardDefinition</c>: the JSON ability schema (mana / activated /
/// triggered) has no shape for a static spell-cost-reduction rider, so
/// this card follows the Goblin Anarchomancer / Medallion-family analogue
/// which builds the <see cref="SpellCostReductionAbility"/> directly.
///
/// ## Deferred (v1 gaps)
/// - <b>Colour-grant interactions</b>: effects that grant a spell extra
///   colours mid-cast (e.g. Painter's Servant naming red) are honoured by
///   <see cref="CardColors.GetColors"/> only insofar as that helper reads
///   the printed mana cost / colour indicator — the same gap shared by
///   every colour-keyed reducer in the engine.
/// </summary>
[CardName("Ruby Medallion")]
public static class RubyMedallionFactory
{
    public const string CardName = "Ruby Medallion";
    public const string PrintedManaCost = "{2}";

    /// <summary>
    /// Construct Ruby Medallion owned and controlled by
    /// <paramref name="owner"/>, with the red-spell cost-reduction rider
    /// attached as static metadata. Cost-calc scan runs at cast time via
    /// <see cref="CostReduction.GetEffectiveCost"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(name: CardName, manaCost: PrintedManaCost);

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 117.7 — "Red spells you cast cost {1} less to cast." Any red
        // spell qualifies (no card-type gate); the {1} reduction touches
        // only generic mana (CR 117.7c), floored at zero in GetEffectiveCost.
        card.AddAbility(new SpellCostReductionAbility(
            predicate: c => CardColors.GetColors(c).Contains(ManaColor.Red),
            reduction: (_, _) => 1,
            description: "Red spells you cast cost {1} less to cast."));

        return card;
    }
}
