using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ghalta, Primal Hunger (Rivals of Ixalan,
/// {10}{G}{G}).
///
/// Legendary Creature — Elder Dinosaur 12/12. Oracle text (verified against
/// Scryfall 2026-06-24):
///   "This spell costs {X} less to cast, where X is the total power of
///    creatures you control.
///    Trample (This creature can deal excess combat damage to the player or
///    planeswalker it's attacking.)"
///
/// The base shape (name, Legendary supertype, Creature type, Elder + Dinosaur
/// subtypes, {10}{G}{G}, 12/12, the <b>Trample</b> keyword marker) is
/// materialised from the embedded JSON definition
/// (<c>ghalta-primal-hunger.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — the JSON <c>keywords</c> line
/// becomes a plain <see cref="KeywordAbility"/>("Trample") marker through the
/// CardDefRuntime keyword path (CR 702.19), same posture as
/// <see cref="ConiferWurmFactory"/>. The self cost-reduction static is layered
/// on here (the JSON schema doesn't express cost reduction), exactly mirroring
/// <see cref="MetalworkColossusFactory"/> / <see cref="EmrakulThePromisedEndFactory"/>.
///
/// ## Implemented (v1)
/// - <b>12/12 Legendary Creature — Elder Dinosaur at {10}{G}{G}</b> (green;
///   two {G} pips — CR 105.2c).
/// - <b>Trample (CR 702.19)</b>: <see cref="KeywordAbility"/>("Trample")
///   marker carried by the JSON keyword line.
/// - <b>Self cost-reduction static (CR 117.7 / CR 601.2f)</b>: "This spell
///   costs {X} less to cast, where X is the total power of creatures you
///   control." Wired via the whole-reduction
///   (<see cref="CostReductionAbility.TotalReducer"/>) shape — the reduction
///   is a live tally ("total power of …"), not a flat per-instance amount, so
///   the function returns the full generic-mana reduction for the caster. The
///   reducer is printed ON the card itself, so
///   <see cref="CostReduction.GetEffectiveCost"/> consults it at cast time and
///   scans the caster's battlefield.
///   <list type="bullet">
///     <item><b>"creatures you control"</b> — predicate is
///       <c>HasType(Creature)</c> over the caster's battlefield. Ghalta itself
///       is on the stack at cost-calc time (not on the battlefield), so it
///       never counts toward its own discount (CR 117.7 / 601.2f — the spell
///       is on the stack while its cost is calculated).</item>
///     <item><b>"total power"</b> — sums <see cref="Creature.Power"/> across
///       the matching permanents. Negative power is not subtracted out (the
///       reduction itself floors at zero — see below).</item>
///     <item><b>"you control"</b> — scoped to the caster's battlefield by
///       <see cref="CostReduction.GetEffectiveCost"/>.</item>
///   </list>
///   CR 117.7c — only generic mana is reduced. Ghalta's printed cost is
///   {10}{G}{G}; the {10} generic is driven down by the total power, the two
///   {G} pips are untouched, and the generic floors at zero (both enforced
///   inside <see cref="CostReduction.GetEffectiveCost"/> via
///   <c>Math.Max(0, …)</c>).
/// </summary>
[CardName("Ghalta, Primal Hunger")]
public static class GhaltaPrimalHungerFactory
{
    public const string CardName = "Ghalta, Primal Hunger";
    public const string Slug = "ghalta-primal-hunger";

    /// <summary>
    /// Construct Ghalta, Primal Hunger owned and controlled by
    /// <paramref name="owner"/>. The base shape + Trample marker come from the
    /// embedded JSON; the self cost-reduction static is layered on here. This
    /// is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Creature, Elder + Dinosaur subtypes, {10}{G}{G}, 12/12, Trample
        // keyword marker). The cost reducer is layered on below (same posture
        // as MetalworkColossusFactory).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // CR 117.7 — "This spell costs {X} less to cast, where X is the total
        // power of creatures you control."
        //
        // Whole-reduction (TotalReducer) shape: the reduction is a live tally,
        // not a flat per-instance amount, so the function returns the full
        // generic-mana reduction for the caster. CR 117.7c — only the {10}
        // generic is reduced (the {G}{G} pips are untouched); the generic
        // floor-at-zero is enforced inside CostReduction.GetEffectiveCost.
        // ----------------------------------------------------------------
        card.AddAbility(new CostReductionAbility(
            totalReducer: TotalPowerOfCreaturesYouControl,
            description:
                "This spell costs {X} less to cast, where X is the total " +
                "power of creatures you control."));

        return card;
    }

    /// <summary>
    /// CR 117.7 — total power of creatures the caster controls. Pure helper
    /// exposed for tests; mirrors the tally consulted by the printed
    /// <see cref="CostReductionAbility.TotalReducer"/>.
    ///
    /// A permanent counts when it has <see cref="CardType.Creature"/> and is
    /// on the caster's battlefield. Power is read from
    /// <see cref="Creature.Power"/> (CR 208.1). Ghalta itself is on the stack
    /// at cost-calc time, so it never contributes to its own discount.
    /// </summary>
    public static int TotalPowerOfCreaturesYouControl(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        var total = 0;
        foreach (var c in caster.Zones.Battlefield.GetCards())
        {
            if (c is Creature creature) total += creature.Power;
        }
        return total;
    }
}
