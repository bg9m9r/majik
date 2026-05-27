using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Anarchomancer (Modern Horizons 2,
/// {R}{G}).
///
/// Creature — Goblin Shaman 1/3. Oracle text:
///   "Red instant and sorcery spells you cast cost {1} less to cast.
///    Green instant and sorcery spells you cast cost {1} less to cast."
///
/// ## Implemented (v1)
/// - Creature — Goblin Shaman, {R}{G}, 1/3, owner/controller wired.
/// - <b>Spell-cost reduction rider (CR 117.7 / CR 601.2f)</b> wired via
///   <see cref="SpellCostReductionAbility"/>. The predicate matches a
///   spell that is BOTH (instant or sorcery) AND (red or green) — the
///   two printed clauses collapse to a single union predicate because
///   each clause grants the SAME {1} reduction and CR 117.7d only
///   counts a reducer once per spell. A red-and-green instant (e.g.
///   Bonecrusher Giant face cast as "Stomp" with hybrid mana) gets the
///   reduction once, not twice — matches printed shape.
/// - Reduction is a flat {1} generic; coloured pips are untouched
///   (CR 117.7c) and the bucket floors at zero inside
///   <see cref="CostReduction.GetEffectiveCost"/>.
/// - "Spells you cast" scope is enforced by
///   <see cref="CostReduction.GetEffectiveCost"/>: only reducers on the
///   caster's battlefield contribute. Multiple Anarchomancers stack
///   linearly (each contributes its own {1}).
///
/// ## Colour determination
///
/// Colour comes from <see cref="CardColors.GetColors"/> which reads the
/// card's printed mana cost (CR 105.2). Hybrid pips contribute both
/// listed colours; Phyrexian pips contribute the named colour. Tokens
/// with explicit colour overrides are handled by CardColors. A spell
/// being cast as a copy or via an alternative cost still carries the
/// original card's colour (CR 706.10 / 117.9), which is what the
/// predicate sees.
///
/// ## Deferred (v1 gaps)
/// - <b>Colour-grant interactions</b>: effects that grant a spell extra
///   colours mid-cast (e.g. Painter's Servant naming red) are honoured
///   by <see cref="CardColors.GetColors"/> only insofar as that helper
///   reads the printed mana cost / token override. A more elaborate
///   "spell colour at cost-calc time" surface is the same gap shared
///   by every colour-keyed reducer in the engine (Mystical Dispute,
///   etc.) — out of scope for this card.
/// </summary>
[CardName("Goblin Anarchomancer")]
public static class GoblinAnarchomancerFactory
{
    public const string CardName = "Goblin Anarchomancer";
    public const string PrintedManaCost = "{R}{G}";
    public const int Power = 1;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Goblin Anarchomancer with the red-or-green
    /// instant/sorcery cost-reduction rider attached as static metadata.
    /// Cost-calc scan runs at cast time via
    /// <see cref="CostReduction.GetEffectiveCost"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 117.7 — "Red instant and sorcery spells you cast cost {1}
        // less to cast. Green instant and sorcery spells you cast cost
        // {1} less to cast." The two printed clauses collapse to a
        // single union predicate ((Instant|Sorcery) AND (Red|Green))
        // because both grant the same {1} reduction and CR 117.7d only
        // counts a reducer once per cast.
        card.AddAbility(new SpellCostReductionAbility(
            predicate: c =>
            {
                if (!c.HasType(CardType.Instant) && !c.HasType(CardType.Sorcery))
                    return false;
                var colors = CardColors.GetColors(c);
                return colors.Contains(ManaColor.Red) || colors.Contains(ManaColor.Green);
            },
            reduction: (_, _) => 1,
            description:
                "Red instant and sorcery spells you cast cost {1} less to cast. " +
                "Green instant and sorcery spells you cast cost {1} less to cast."));

        return card;
    }
}
