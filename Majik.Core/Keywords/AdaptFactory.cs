using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.116 — Adapt N. "If this creature has no +1/+1 counters on it,
/// put N +1/+1 counters on it." Adapt is an activated-ability keyword
/// from Ravnica Allegiance (Simic). The printed text is the keyword on
/// one side of a colon and the cost on the other; resolution is gated
/// by an intervening-if that re-checks the "no +1/+1 counters" predicate
/// at resolution time (CR 702.116b).
///
/// <para>
/// Canonical edge cases (CR 702.116a–c):
/// <list type="bullet">
///   <item><b>Already-adapted creature</b> (CR 702.116b): the activation
///   still succeeds and the ability still resolves, but no counters are
///   placed. The "if" is an intervening-if (CR 603.4), checked once when
///   the ability would go on the stack and again on resolution; this
///   helper treats the resolve-time check as authoritative so a player
///   who pays the cost and then has counters arrive between activation
///   and resolution (rare — counters added by another player's effect)
///   sees the placement fizzle.</item>
///   <item><b>Replacement effects on the placement</b> (CR 702.116a):
///   counters are routed through <see cref="CountersService.Add"/> so
///   Hardened Scales / Doubling Season modify the post-Adapt count.</item>
///   <item><b>Multiple Adapt abilities</b> (CR 702.116c): each activated
///   ability built by this helper is independent; activating one does
///   NOT prevent activating another. The resolve-time check on each
///   ensures only the first to resolve places counters (subsequent
///   activations fizzle on the "no +1/+1 counters" gate).</item>
/// </list>
/// </para>
///
/// <para>
/// Wiring posture mirrors <see cref="ProwessFactory"/> /
/// <see cref="ModularFactory"/>: <see cref="Build"/> returns the activated
/// ability plus stamps a <see cref="KeywordAbility"/> marker so card
/// inspectors / tooltips can see the "Adapt N" reminder text. The
/// activated ability uses <see cref="CountersService.Add"/> for the
/// placement so the <paramref name="replacements"/> bus rewrites
/// (Hardened Scales) and the post-commit <see cref="CounterAddedEvent"/>
/// publish both fire — this is the surface that "Whenever one or more
/// +1/+1 counters are put on this creature" triggers (Emperor of Bones,
/// Conclave Mentor) subscribe to.
/// </para>
/// </summary>
public static class AdaptFactory
{
    /// <summary>
    /// Build an Adapt-N activated ability for <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The creature carrying the Adapt keyword.
    /// Must be non-null and have an owner.</param>
    /// <param name="cost">Mana-cost portion of the activated ability
    /// (e.g. <c>"{1}{B}"</c> for Emperor of Bones, <c>"{4}{G}{U}"</c>
    /// for Hydroid Krasis-style activated Adapts). Must be non-null /
    /// non-empty.</param>
    /// <param name="amount">N — number of +1/+1 counters placed when the
    /// "no +1/+1 counters" gate succeeds. Must be &gt; 0; printed Adapt
    /// values are always positive.</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/>
    /// routed through <see cref="CountersService.Add"/> for the
    /// placement.</param>
    /// <param name="eventBus">Optional <see cref="IEventBus"/> the
    /// post-commit <see cref="CounterAddedEvent"/> publishes on. When
    /// null, the placement still commits but no event surfaces — so the
    /// "Whenever +1/+1 counters are put on" trigger family doesn't fire
    /// (suitable for shape tests).</param>
    /// <returns>The configured activated ability. The caller is expected
    /// to stamp it on the source with
    /// <see cref="Permanent.AddAbility(Majik.Core.Abilities.IAbility)"/>
    /// (the factory does not call AddAbility so callers can compose
    /// further before mounting).</returns>
    public static ActivatedAbility Build(
        Creature source,
        string cost,
        int amount,
        ReplacementBus? replacements = null,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrEmpty(cost))
            throw new ArgumentException("Adapt cost must be non-empty.", nameof(cost));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "Adapt N must be positive.");

        var controller = source.Controller
            ?? source.Owner
            ?? throw new InvalidOperationException(
                "Adapt source must have a controller or owner.");

        // CR 702.116 — keyword marker so inspectors / tooltips can see
        // "Adapt N". Value-only; the activated ability does the work.
        source.AddAbility(new KeywordAbility($"Adapt {amount}", source, controller));

        // CR 702.116a / 702.116b — activated ability whose effect places
        // N +1/+1 counters on the source IFF the source currently has
        // none. The "no counters" check is performed at resolution time;
        // an Adapt activation that would otherwise add counters fizzles
        // if the creature already has +1/+1 counters when the ability
        // resolves.
        var effect = new Effect(
            $"Adapt {amount} (CR 702.116) — put {amount} +1/+1 counters on " +
            $"{source.Name} if it has no +1/+1 counters",
            () =>
            {
                if (source.Zone != Majik.Core.Zones.ZoneType.Battlefield) return;

                // CR 702.116b — "If this creature has no +1/+1 counters
                // on it." Resolution-time intervening-if check.
                if (source.Counters.Count(CounterType.PlusOnePlusOne) > 0) return;

                CountersService.Add(
                    source, CounterType.PlusOnePlusOne, amount,
                    replacements, eventBus);
            });

        return new ActivatedAbility(
            source: source,
            controller: controller,
            costs: new ICost[] { new ManaCostCost(cost) },
            effects: new IEffect[] { effect });
    }
}
