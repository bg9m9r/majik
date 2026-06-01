using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.30 — Echo [cost]. "At the beginning of your upkeep, if this permanent
/// came under your control since the beginning of your last upkeep, sacrifice
/// it unless you pay its echo cost."
///
/// <para>The "came under your control since your last upkeep" condition
/// (CR 702.30b) is the distinctive part: a permanent owes echo on the first
/// upkeep after it enters / changes control, and only that once. This helper
/// models it with a per-card "echo owed" latch set when the permanent comes
/// under control (<see cref="AttachTo"/>) and cleared the first time the
/// controller's upkeep resolves the echo trigger (paid or sacrificed).</para>
///
/// <para>The pay-or-sacrifice decision is supplied by the caller as a
/// <c>willPay</c> predicate so the mechanic stays UI-agnostic: the runtime
/// passes an agent-driven decision (and only offers "pay" when the controller
/// can actually afford the cost), and tests pass a deterministic choice.</para>
/// </summary>
public static class EchoHelper
{
    /// <summary>
    /// Build and (optionally) register the Echo upkeep trigger on
    /// <paramref name="permanent"/>. The trigger fires at the beginning of the
    /// controller's upkeep; on resolution it consults <paramref name="willPay"/>
    /// (only when the controller can afford <paramref name="echoCost"/>) and
    /// either pays the echo cost or sacrifices the permanent — then clears the
    /// echo debt so it never fires again (CR 702.30b).
    /// </summary>
    /// <param name="permanent">The permanent with echo.</param>
    /// <param name="echoCost">The echo cost (CR 702.30a).</param>
    /// <param name="willPay">Decision: returns true to pay the echo cost. Only
    /// invoked when the controller can afford the cost. Defaults to "always pay
    /// when affordable" when null.</param>
    /// <param name="triggers">When supplied, the upkeep trigger is registered so
    /// a controller-upkeep <see cref="Events.StepStartedEvent"/> queues it.</param>
    public static TriggeredAbility AttachTo(
        Permanent permanent,
        ManaCost echoCost,
        Func<Permanent, bool>? willPay = null,
        TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(permanent);
        ArgumentNullException.ThrowIfNull(echoCost);

        var controller = permanent.Controller ?? permanent.Owner!;
        // CR 702.30b — echo is owed on the next upkeep after coming under control.
        var owed = new EchoDebt { Pending = true };

        var effect = new Effect(
            $"{permanent.Name}: Echo {echoCost} — pay or sacrifice",
            () => ResolveEcho(permanent, echoCost, willPay, owed));

        // CR 702.30 — "at the beginning of your upkeep, IF [came under control
        // since last upkeep]". The intervening-if (CR 603.4) keeps the trigger
        // off the stack once the debt is cleared.
        var trigger = new TriggeredAbility(
            source: permanent,
            controller: controller,
            condition: Triggers.OnStepBegin(controller, PhaseStateType.Upkeep),
            effects: new IEffect[] { effect },
            interveningIf: () => owed.Pending,
            activeZones: new[] { ZoneType.Battlefield });

        permanent.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
        return trigger;
    }

    /// <summary>
    /// Resolve one echo upkeep: pay the cost (when affordable and chosen) or
    /// sacrifice the permanent (CR 702.30c). The echo debt is cleared either
    /// way so it never fires again. Exposed for factories that build their own
    /// trigger shape.
    /// </summary>
    public static void ResolveEcho(
        Permanent permanent,
        ManaCost echoCost,
        Func<Permanent, bool>? willPay,
        EchoDebt owed)
    {
        ArgumentNullException.ThrowIfNull(permanent);
        ArgumentNullException.ThrowIfNull(echoCost);
        ArgumentNullException.ThrowIfNull(owed);

        if (!owed.Pending) return;
        // CR 702.30b — the debt is satisfied this upkeep regardless of the
        // pay/sacrifice outcome; clear it up front so a sacrifice that re-enters
        // the trigger machinery can't double-charge.
        owed.Pending = false;

        var controller = permanent.Controller ?? permanent.Owner!;
        var canAfford = controller.ManaPool.CanPay(echoCost);
        var pay = canAfford && (willPay?.Invoke(permanent) ?? true);

        if (pay)
        {
            controller.PayMana(echoCost);
            return;
        }

        // CR 702.30c — couldn't or chose not to pay → sacrifice.
        if (permanent.Zone == ZoneType.Battlefield)
            Fx.Sacrifice(permanent);
    }

    /// <summary>Mutable per-card echo-debt latch (CR 702.30b).</summary>
    public sealed class EchoDebt
    {
        public bool Pending { get; set; }
    }
}
