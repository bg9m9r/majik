using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Of One Mind (Modern Horizons, {2}{U}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-02):
///   "This spell costs {2} less to cast if you control a Human creature and a
///    non-Human creature.
///    Draw two cards."
///
/// ## Implemented (v1)
/// - <b>Sorcery shape</b> at printed cost {2}{U} (mana value 3, blue). The
///   base card shape (name / Sorcery type / {2}{U} cost) is materialised from
///   the embedded JSON definition (<c>of-one-mind.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="BeholdTheMultiverseFactory"/>.
/// - <b>Conditional {2}-less cost reduction (CR 117.7)</b>: wired via the
///   whole-reduction <see cref="CostReductionAbility"/> overload (the same
///   shape Domain uses). The reducer is printed on the card itself and is
///   consulted at cast time by <see cref="CostReduction.GetEffectiveCost"/>,
///   which scans the caster's battlefield. The reduction is a flat {2} when —
///   and only when — the caster controls at least one Human creature AND at
///   least one non-Human creature; otherwise {0}. A single creature can only
///   satisfy one half of the clause (a Human is never a non-Human), so the
///   two predicates are evaluated independently across the battlefield.
///   Coloured pips are untouched (CR 117.7c) and floor-at-zero is enforced
///   inside <see cref="CostReduction.GetEffectiveCost"/>, so the lone {U} pip
///   always survives — the headline turn casts Of One Mind for {U}.
/// - <b>Resolve effect (via <see cref="BuildResolveEffect"/>)</b>: draw two
///   cards (CR 121.1). Routed through <see cref="Fx.DrawCards"/> so any active
///   replacement effect (Dredge etc.) gets a shot per draw; a library that
///   empties mid-draw flags the SBA-driven loss (CR 704.5b) via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> inside Fx without
///   throwing. Same draw shape as <see cref="DivinationFactory"/>.
///
/// ## Rules citations
/// - CR 117.5 — printed mana cost.
/// - CR 117.7 / 117.7c — cost reduction; only generic reduces, floor at zero.
/// - CR 121.1 — draw two cards.
/// - CR 704.5b — draw-from-empty-library loss.
/// </summary>
[CardName("Of One Mind")]
public static class OfOneMindFactory
{
    public const string CardName = "Of One Mind";
    public const string PrintedManaCost = "{2}{U}";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "of-one-mind";

    /// <summary>Generic-mana reduction granted when the board condition holds
    /// (CR 117.7).</summary>
    public const int ReductionAmount = 2;

    private const int DrawAmount = 2;

    /// <summary>
    /// Build Of One Mind from the embedded JSON definition and attach the
    /// conditional {2}-less cost reducer. The "draw two" resolve body is built
    /// on demand via <see cref="BuildResolveEffect"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        // CR 117.7 — "This spell costs {2} less to cast if you control a Human
        // creature and a non-Human creature." Whole-reduction shape: the
        // function is called once per cast with the caster and returns the
        // total generic reduction. CostReduction.GetEffectiveCost scans only
        // the caster's battlefield, so the "you control" scope is enforced by
        // the cost-calc helper. A single creature satisfies at most one half of
        // the clause — a Human is never a non-Human — so the two predicates are
        // evaluated independently (the same creature can never be counted for
        // both). Floor-at-zero (CR 117.7c) is enforced in the cost-calc helper.
        card.AddAbility(new CostReductionAbility(
            totalReducer: ControlsHumanAndNonHuman,
            description: "This spell costs {2} less to cast if you control a Human creature and a non-Human creature."));

        return card;
    }

    /// <summary>
    /// Whole-reduction predicate: {2} generic off when the caster controls at
    /// least one Human creature AND at least one non-Human creature, else {0}
    /// (CR 117.7). Only creatures count toward either half of the clause.
    /// </summary>
    public static int ControlsHumanAndNonHuman(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        var creatures = caster.Zones.Battlefield.GetCards()
            .Where(c => c.HasType(CardType.Creature))
            .ToList();

        var hasHuman = creatures.Any(c => c.HasSubtype(CardSubtype.Human));
        var hasNonHuman = creatures.Any(c => !c.HasSubtype(CardSubtype.Human));

        return hasHuman && hasNonHuman ? ReductionAmount : 0;
    }

    /// <summary>
    /// Build Of One Mind's resolve effect — draw two cards (CR 121.1). Routed
    /// through <see cref="Fx.DrawCards"/> so the replacement bus gets a shot
    /// per draw and an empty library stamps the SBA loss flag (CR 704.5b)
    /// without throwing. Same shape as <see cref="DivinationFactory"/>.
    /// </summary>
    /// <param name="caster">Of One Mind's controller; draws the two cards.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: draw two cards.",
                () =>
                {
                    // CR 121.1 — draw 2. Replacement bus per-draw; empty
                    // library stamps the SBA loss flag (CR 704.5b).
                    Fx.DrawCards(caster, DrawAmount);
                }),
        };
    }
}
