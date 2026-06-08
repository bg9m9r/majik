using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.Abilities;

/// <summary>
/// CR 606 — a loyalty ability. The number printed in the cost box is
/// added to / subtracted from the planeswalker's loyalty as part of
/// activation cost. Only one loyalty ability may be activated per
/// planeswalker per turn (CR 606.5), tracked by
/// <see cref="Permanent.LoyaltyAbilityActivatedThisTurn"/>.
///
/// <para>
/// CR 606.3 — a loyalty ability may be activated only at sorcery speed
/// (the controller's main phase, with an empty stack, while they hold
/// priority). The loyalty cost is paid as the ability is put on the stack
/// (CR 606.3/606.5); the EFFECT then resolves later off the stack — so a
/// loyalty ability is a normal targetable, respondable activated ability.
/// </para>
///
/// <para>
/// This type holds the ability's <see cref="Effects"/> + optional
/// <see cref="TargetRequests"/> and serves as a template: the dispatch path
/// (<c>TurnDriver.DispatchActivate</c>) pays the loyalty cost on announcement
/// and builds an <see cref="ActivatedAbility"/> stack object from this
/// template (source = the planeswalker, controller, chosen targets,
/// <c>costs:</c> empty since the loyalty cost is pre-paid, <c>effects:</c> the
/// loyalty effects). That stack object resolves via the existing resolver +
/// <see cref="ResolutionContext"/>, so targeted loyalty effects read
/// <see cref="ResolutionContext"/> chosen targets / <c>rc.Source</c>.
/// </para>
///
/// <see cref="LoyaltyChange"/>: positive = "+N" (add counters),
/// negative = "-N" (remove counters; activation illegal if not enough
/// loyalty), zero = "0:" abilities.
/// </summary>
public sealed class LoyaltyAbility : IAbility
{
    private readonly List<IEffect> _effects = new();

    public Planeswalker Source { get; }
    public int LoyaltyChange { get; }

    /// <summary>
    /// The effects this loyalty ability resolves off the stack (CR 608).
    /// Read by the dispatch path when it builds the stack object.
    /// </summary>
    public IReadOnlyList<IEffect> Effects => _effects.AsReadOnly();

    /// <summary>
    /// CR 602.2b — the targets the activating player's agent must choose when
    /// this loyalty ability is put on the stack. Empty for non-targeted
    /// loyalty abilities (most of them).
    /// </summary>
    public IReadOnlyList<TargetRequest> TargetRequests { get; }

    /// <summary>
    /// CR 606.3 — loyalty abilities are always sorcery-speed. Always true.
    /// </summary>
    public bool IsSorcerySpeed => true;

    /// <summary>
    /// Stack-resolved constructor (preferred). The effects resolve off the
    /// stack; targeted effects read chosen targets / source from the live
    /// <see cref="ResolutionContext"/>.
    /// </summary>
    public LoyaltyAbility(
        Planeswalker source,
        int loyaltyChange,
        IEnumerable<IEffect> effects,
        IEnumerable<TargetRequest>? targetRequests = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        LoyaltyChange = loyaltyChange;
        if (effects != null) _effects.AddRange(effects);
        TargetRequests = targetRequests is null
            ? Array.Empty<TargetRequest>()
            : targetRequests.ToList().AsReadOnly();
    }

    /// <summary>
    /// Back-compat constructor — wraps a synchronous, non-targeted
    /// <paramref name="effect"/> as a single inline <see cref="IEffect"/>.
    /// The body runs at resolution off the stack (CR 608). Existing factories
    /// that capture their controller / resolvers in a closure keep working
    /// unchanged; only the timing shifts from "immediate" to "on resolution".
    /// </summary>
    public LoyaltyAbility(Planeswalker source, int loyaltyChange, Action effect)
        : this(
            source,
            loyaltyChange,
            new[] { Fx.Inline($"Loyalty {SignedDescription(loyaltyChange)}", effect ?? throw new ArgumentNullException(nameof(effect))) })
    {
    }

    public string Description => SignedDescription(LoyaltyChange);

    private static string SignedDescription(int change) => change switch
    {
        > 0 => $"+{change}",
        < 0 => change.ToString(),
        _ => "0",
    };

    /// <summary>
    /// CR 606.3/606.5 — true when this loyalty ability may currently be
    /// activated: its planeswalker hasn't already activated a loyalty ability
    /// this turn, and (for a minus ability) it has enough loyalty to pay the
    /// cost. The sorcery-speed timing window is enforced separately by the
    /// enumerator / dispatcher.
    /// </summary>
    public bool CanActivate()
    {
        if (Source.LoyaltyAbilityActivatedThisTurn) return false;
        if (LoyaltyChange < 0 && Source.Loyalty + LoyaltyChange < 0) return false;
        return true;
    }

    /// <summary>
    /// CR 606.3/606.5 — pay the loyalty cost (add/remove loyalty + mark the
    /// once-per-turn flag). Called by the dispatch path as the ability is put
    /// on the stack, BEFORE the stack object's effects resolve. Throws if the
    /// ability cannot currently be activated.
    /// </summary>
    public void PayLoyaltyCost()
    {
        if (!CanActivate())
            throw new InvalidOperationException("Loyalty ability cannot be activated");

        if (LoyaltyChange > 0) Source.AddLoyalty(LoyaltyChange);
        else if (LoyaltyChange < 0) Source.RemoveLoyalty(-LoyaltyChange);

        Source.LoyaltyAbilityActivatedThisTurn = true;
    }

    /// <summary>
    /// Legacy synchronous activation — pays the loyalty cost and runs the
    /// effects immediately (bypassing the stack). Retained for the direct-
    /// activation tests that predate the priority-loop gameplay path; real
    /// matches go through the priority loop / dispatch path instead.
    /// </summary>
    public void Activate()
    {
        PayLoyaltyCost();
        foreach (var effect in _effects)
        {
            effect.Execute();
        }
    }
}
