using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// CR 117.7 — "This spell costs {N} less to cast for each X you control."
/// Printed on the card itself; consulted at cast time by
/// <see cref="CostReduction.GetEffectiveCost"/> to lower the spell's
/// generic-mana requirement. Cannot reduce coloured pips (CR 117.7c).
///
/// Instances are static metadata — no resolution / trigger semantics —
/// so they live on the card's <see cref="ICard.Abilities"/> list and are
/// scanned at cost-calculation time.
///
/// Two shapes are supported:
/// 1. Per-instance + predicate (Affinity / Affinity-for-basic-type): the
///    caster's battlefield is scanned, each matching card contributes
///    <see cref="PerInstance"/> generic.
/// 2. Whole-reduction function (<see cref="TotalReducer"/>): the
///    function is called once per cast with the caster and returns the
///    total generic reduction to apply. Used by Domain
///    (CR 702.16 — Scion of Draco / Tribal Flames family) where the
///    reduction is "{N} per distinct basic land type" rather than per
///    instance.
/// </summary>
public sealed class CostReductionAbility : IAbility
{
    /// <summary>How many generic mana to remove per matching object the
    /// caster controls. Zero when <see cref="TotalReducer"/> is used.</summary>
    public int PerInstance { get; }

    /// <summary>Predicate matching cards on the caster's battlefield that
    /// count toward the reduction (e.g. all artifacts). Unused when
    /// <see cref="TotalReducer"/> is supplied.</summary>
    public Func<ICard, bool> Predicate { get; }

    /// <summary>Optional whole-reduction computation. When non-null, this
    /// replaces the per-instance scan: the function returns the total
    /// generic reduction to apply for a given caster (e.g. Domain — count
    /// distinct basic land types × {N}).</summary>
    public Func<Player, int>? TotalReducer { get; }

    public string Description { get; }

    public CostReductionAbility(int perInstance, Func<ICard, bool> predicate, string description)
    {
        if (perInstance <= 0) throw new ArgumentOutOfRangeException(nameof(perInstance));
        PerInstance = perInstance;
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        TotalReducer = null;
        Description = description ?? string.Empty;
    }

    /// <summary>Construct a whole-reduction cost reducer (e.g. Domain).
    /// <paramref name="totalReducer"/> returns the full generic-mana
    /// reduction for the given caster; floor-at-zero is enforced in
    /// <see cref="CostReduction.GetEffectiveCost"/>.</summary>
    public CostReductionAbility(Func<Player, int> totalReducer, string description)
    {
        TotalReducer = totalReducer ?? throw new ArgumentNullException(nameof(totalReducer));
        PerInstance = 0;
        Predicate = static _ => false;
        Description = description ?? string.Empty;
    }

    public static CostReductionAbility AffinityFor(CardType type) =>
        new(1, c => c.HasType(type), $"Affinity for {type.ToString().ToLowerInvariant()}s");
}

/// <summary>
/// Cost-calculation entry point. Pure function — no side effects. Called
/// by <see cref="Majik.Core.Game.SpellCastFlow"/> for the actual payment
/// cost and by HeuristicBotAgent's mana picker for affordability.
/// </summary>
public static class CostReduction
{
    public static ManaCost GetEffectiveCost(ICard card, Player caster)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (caster == null) throw new ArgumentNullException(nameof(caster));

        var cost = ManaCost.Parse(card.ManaCost ?? "");
        var reducers = card.Abilities.OfType<CostReductionAbility>().ToList();
        if (reducers.Count == 0) return cost;

        var battlefield = caster.Zones.Battlefield.GetCards().ToList();
        var totalReduction = 0;
        foreach (var r in reducers)
        {
            if (r.TotalReducer != null)
            {
                // Whole-reduction shape (Domain et al.). The function
                // owns its semantics — distinct-basic-type counting for
                // Domain is computed against the caster's battlefield
                // and may dwarf the printed generic; floor-at-zero is
                // enforced below.
                totalReduction += Math.Max(0, r.TotalReducer(caster));
                continue;
            }

            // The spell itself doesn't count toward its own Affinity
            // discount (it's still on the stack at cost-calc time, not
            // battlefield); excluding by InstanceId is defensive.
            var count = battlefield.Count(c =>
                c.InstanceId != card.InstanceId && r.Predicate(c));
            totalReduction += count * r.PerInstance;
        }
        return cost.WithGeneric(Math.Max(0, cost.Generic - totalReduction));
    }
}
