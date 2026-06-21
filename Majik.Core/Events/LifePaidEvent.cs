using Majik.Core.Players;

namespace Majik.Core.Events;

/// <summary>
/// CR 118.8 / CR 119.4 — event fired the moment a player <b>pays life</b> as a
/// cost (a "Pay N life" cost or additional cost, NOT plain life loss). Published
/// AFTER the life has been debited, mirroring the
/// <see cref="PermanentSacrificedEvent"/> / <see cref="DiscardedEvent"/>
/// post-payment posture.
///
/// <para>
/// This is the dedicated life-PAYMENT provenance surface that a "Whenever a
/// player pays life, …" payoff subscribes to. Unlike a raw
/// <see cref="LifeChangedEvent"/> life-decrease condition — which can't
/// distinguish a life PAYMENT (a cost — CR 118.8) from any other life decrease
/// (burn damage, a drain spell, an effect's "lose N life", paying life to a
/// replacement) — this event fires ONLY on a real pay-life COST and carries
/// enough context to filter correctly:
/// <list type="bullet">
///   <item><see cref="Player"/> — the paying player, so a "Whenever <b>you</b>
///     pay life …" clause can scope to its own controller (CR 109.5).</item>
///   <item><see cref="Amount"/> — how much life was paid (CR 119.4 — paying 0
///     life is not "paying life", so this is always &gt; 0).</item>
///   <item><see cref="WasCost"/> — whether the life was paid as a cost. This is
///     always <see langword="true"/> today (every publish site is a cost seam);
///     it is carried for symmetry with <see cref="DiscardedEvent.WasCost"/> /
///     to future-proof a non-cost life-payment shape (e.g. Plague of Vermin's
///     "each player may pay any amount of life", which pays life as part of a
///     spell's resolution rather than as a cost).</item>
/// </list>
/// </para>
///
/// <para>
/// Published by the central pay-life cost seam: the bus-aware
/// <see cref="Majik.Core.Costs.AdditionalCost"/> /
/// <see cref="Majik.Core.Costs.PayLifeCost"/> when paid through
/// <see cref="Majik.Core.Costs.CostPayment.PayCosts(Player, System.Collections.Generic.IEnumerable{Majik.Core.Costs.ICost}, Mana.ManaSpendContext, IEventBus)"/>
/// (the prod cast / ability-activation cost path — same seam that publishes
/// <see cref="PermanentSacrificedEvent"/> for a sac cost), plus the shock-land
/// "as it enters, you may pay 2 life" ETB
/// (<see cref="Majik.Core.Effects.ShockLandReplacement"/>, which looks the bus
/// up best-effort via <see cref="EventBusRegistry.Get(Player?)"/>). A pay-life
/// cost paid WITHOUT a bus (direct-construction unit tests, the bus-less legacy
/// <see cref="Majik.Core.Costs.ICost.Pay(Player)"/> path) still debits life but
/// publishes nothing — exactly like the sacrifice / discard seams.
/// </para>
/// </summary>
public class LifePaidEvent : GameEvent
{
    /// <summary>The player who paid the life (CR 118.8). The triggering player
    /// a "whenever you pay life …" clause scopes against (CR 109.5).</summary>
    public Player Player { get; }

    /// <summary>How much life was paid. Always &gt; 0 — paying 0 life is not
    /// "paying life" (CR 119.4), so the publish site suppresses a 0 payment.</summary>
    public int Amount { get; }

    /// <summary>Whether the life was paid as a cost (CR 118.8). Always
    /// <see langword="true"/> for every current publish site; carried for
    /// symmetry with <see cref="DiscardedEvent.WasCost"/> and to future-proof a
    /// resolution-time "pay life" shape that is not a cost.</summary>
    public bool WasCost { get; }

    public LifePaidEvent(Player player, int amount, bool wasCost)
        : base()
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount),
                "A LifePaidEvent must carry a positive amount — paying 0 life is not paying life (CR 119.4).");
        Amount = amount;
        WasCost = wasCost;
    }
}
