using Majik.Core.Cards;
using Majik.Core.Costs;

namespace Majik.Core.Primitives;

/// <summary>
/// Shared cost-primitive facade — the cost-side companion to
/// <see cref="Fx"/> (effects) and <see cref="Majik.Core.Abilities.Triggers"/>
/// (triggers). One discoverable home for the activation-cost shapes that
/// the declarative card systems materialize: <c>{T}</c> (tap this),
/// "sacrifice this", a mana cost, "remove N +1/+1 counters", and
/// "discard this card".
///
/// <para>
/// Each method returns a ready-to-attach <see cref="ICost"/>. The shapes
/// re-export the existing cost types (<see cref="AdditionalCost"/>,
/// <see cref="ManaCostCost"/>, <see cref="RemovePlusOnePlusOneCounterCost"/>,
/// <see cref="DiscardSelfCost"/>) so a factory can write
/// <c>Costs.TapSelf(permanent)</c> instead of reaching into the
/// <c>Majik.Core.Costs</c> namespace and picking the right ctor/factory.
/// </para>
///
/// <para>
/// Convergence note (PLAN 03): both declarative card vocabularies — the
/// fluent <see cref="Majik.Core.CardData.Definitions.CardDef"/> DSL and the
/// JSON <see cref="Majik.Core.CardData.Definitions.CardDefinition"/>
/// schema — route their cost construction through this helper. The DSL
/// <see cref="Majik.Core.CardData.Definitions.CardDef"/> is the target
/// model; JSON deserializes into it; one
/// <see cref="Fx"/>/<see cref="Majik.Core.Abilities.Triggers"/>/<see cref="Costs"/>
/// vocabulary backs both. Keep additions to this class additive (PLAN 03
/// Slices 1–2 are behaviour-neutral; new cost shapes must not change what
/// existing cards produce).
/// </para>
///
/// <para>
/// All entry points are static and free of hidden state. Methods are
/// shaped to stay async-ready (cost payment itself remains synchronous on
/// <see cref="ICost"/> today; these are pure constructors with no I/O), so
/// when the cost-payment surface gains an async path the call sites do not
/// move.
/// </para>
/// </summary>
public static class Costs
{
    /// <summary>
    /// CR 602.5 / 605.3a — the <c>{T}</c> activation cost: tap
    /// <paramref name="permanent"/> as part of paying for an ability.
    /// Mirrors the inlined <c>AdditionalCost.Tap(permanent)</c> that the
    /// JSON <c>tap_self</c> cost produced before consolidation.
    /// </summary>
    public static ICost TapSelf(Permanent permanent)
    {
        ArgumentNullException.ThrowIfNull(permanent);
        return AdditionalCost.Tap(permanent);
    }

    /// <summary>
    /// CR 701.16 — "Sacrifice this permanent" activation cost.
    /// Mirrors the inlined <c>AdditionalCost.Sacrifice(permanent)</c> the
    /// JSON <c>sacrifice_self</c> cost produced before consolidation.
    /// </summary>
    public static ICost SacrificeSelf(Permanent permanent)
    {
        ArgumentNullException.ThrowIfNull(permanent);
        return AdditionalCost.Sacrifice(permanent);
    }

    /// <summary>
    /// CR 601.2f — a mana activation cost. <paramref name="manaCostString"/>
    /// accepts bracketed (<c>"{1}{R}"</c>) or unbracketed (<c>"1R"</c>)
    /// forms; an empty / whitespace string yields a zero cost. Mirrors the
    /// inlined <c>new ManaCostCost(amount)</c> the JSON <c>mana</c> cost
    /// produced before consolidation.
    /// </summary>
    public static ICost Mana(string manaCostString)
        => new ManaCostCost(manaCostString ?? string.Empty);

    /// <summary>
    /// CR 118 — a mana activation cost from a parsed
    /// <see cref="Majik.Core.ValueObjects.ManaCost"/> value object.
    /// </summary>
    public static ICost Mana(Majik.Core.ValueObjects.ManaCost manaCost)
    {
        ArgumentNullException.ThrowIfNull(manaCost);
        return new ManaCostCost(manaCost);
    }

    /// <summary>
    /// CR 122 / 602.5 — "Remove <paramref name="amount"/> +1/+1 counters
    /// from <paramref name="source"/>" activation cost (Walking Ballista's
    /// ping). Mirrors the inlined
    /// <c>new RemovePlusOnePlusOneCounterCost(permanent, amount)</c> the
    /// JSON <c>remove_counter</c> cost (self / +1/+1) produced before
    /// consolidation.
    /// </summary>
    public static ICost RemovePlusOnePlusOneCounter(Permanent source, int amount = 1)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new RemovePlusOnePlusOneCounterCost(source, amount);
    }

    /// <summary>
    /// CR 702.74 — "Discard this card" activation cost (Channel and the
    /// discard-self family). Activation zone is the hand. Mirrors the
    /// inlined <c>new DiscardSelfCost(self)</c> the JSON <c>discard_self</c>
    /// cost produced before consolidation.
    /// </summary>
    public static ICost DiscardSelf(ICard self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return new DiscardSelfCost(self);
    }
}
