using System;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.49 — <b>Echo {cost}</b>. A keyword that represents a triggered
/// ability:
///
/// <para>CR 702.49a — "Echo [cost]" means "At the beginning of your upkeep, if
/// this permanent came under your control since the beginning of your most
/// recent upkeep, sacrifice it unless you pay [cost]."</para>
///
/// <para>This is the "one-shot, lifts-on-the-first-upkeep" sibling of the
/// recurring upkeep pay-or-consequence family (Stasis / Kataki / the pact
/// cycle): it rides the exact same
/// <see cref="Majik.Core.Primitives.UpkeepPayUnlessConsequence"/> resolution
/// primitive (CR 117.1 — "sacrifice unless you pay"), and adds one gating flag
/// on top: the trigger fires only once — at the FIRST upkeep after the
/// permanent entered / changed control — then never again. Cumulative Upkeep
/// (CR 702.24) is the recurring, age-counter-scaling variant of the same seam;
/// Echo is the single-shot variant.</para>
///
/// <para>The "came under your control since your last upkeep" gate (CR 702.49a)
/// is modelled by a captured one-shot flag (<c>echoUnpaid</c>) instead of a new
/// per-game upkeep ledger: it starts <c>true</c> when the permanent enters
/// under its controller and is cleared the first time the echo trigger goes on
/// the stack / resolves. Re-entering the battlefield builds a fresh echo
/// ability (a new game object, a new closure), so the flag is naturally re-armed
/// — matching CR 702.49d (a permanent that changes control or is a new object
/// re-arms echo). Once paid or sacrificed, the closure never re-fires, so a
/// permanent that pays its echo and stays in play is taxed exactly once.</para>
///
/// <para>The consequence verb is a raw Battlefield → Graveyard sacrifice
/// (CR 701.16), the same shape Stasis / Kataki use for "sacrifice this unless
/// you pay". The legacy / shape-only sync <c>Execute()</c> path preserves the
/// deterministic "pay if able, else sacrifice" posture; the live engine prompts
/// the controller's agent "Pay [echo cost]?" (CR 117.1).</para>
/// </summary>
public static class EchoFactory
{
    /// <summary>
    /// Build the Echo triggered ability for <paramref name="source"/> with the
    /// given <paramref name="echoCost"/> (e.g. "{1}{R}" for Mogg War Marshal).
    /// The ability fires at the controller's upkeep, is gated by a one-shot
    /// intervening-if (echo unpaid + still on the battlefield, CR 603.4), and
    /// at resolution pays the echo cost or sacrifices the permanent.
    /// </summary>
    public static TriggeredAbility Build(Permanent source, ManaCost echoCost)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(echoCost);

        var controller = source.Controller ?? source.Owner
            ?? throw new InvalidOperationException(
                "Echo source must have a controller or owner.");

        // CR 702.49a — "came under your control since the beginning of your
        // most recent upkeep." Captured one-shot flag: armed when the permanent
        // enters under control (construction time), cleared the first time the
        // echo ability is taken to the stack / resolved. A re-entering object
        // builds a fresh closure, naturally re-arming echo (CR 702.49d).
        var echoUnpaid = true;

        // CR 117.1 / CR 701.16 — "sacrifice it unless you pay [cost]." Rides the
        // shared upkeep pay-or-consequence primitive; the consequence is a raw
        // Battlefield → Graveyard sacrifice (same shape Stasis / Kataki use).
        var payOrSac = Majik.Core.Primitives.UpkeepPayUnlessConsequence.Build(
            $"Echo {echoCost}: at upkeep, sacrifice unless you pay the echo cost",
            controller,
            echoCost,
            consequence: () =>
            {
                var payer = source.Controller ?? controller;
                payer.Zones.Battlefield.RemoveCard(source);
                payer.Zones.Graveyard.AddCard(source);
                source.SetZone(ZoneType.Graveyard);
            },
            promptText: $"Pay the echo cost {echoCost} to keep {source.Name}?",
            // CR 603.4 — re-check the echo gate at resolution: only act while the
            // echo is still unpaid AND the permanent is still on the battlefield.
            guard: () => echoUnpaid && source.Zone == ZoneType.Battlefield);

        // Wrap the pay-or-sac so the one-shot flag is cleared after the echo
        // resolves once — paid or sacrificed, echo never fires again for this
        // object (CR 702.49a — it only fires on the FIRST qualifying upkeep).
        // The inner primitive owns the affordability probe + agent prompt + the
        // sacrifice consequence; this wrapper only disarms the gate afterward.
        var effect = new Effect(payOrSac.Description, async ctx =>
        {
            await payOrSac.ExecuteAsync(ctx).ConfigureAwait(false);
            echoUnpaid = false;
        });

        return new TriggeredAbility(
            source: source,
            controller: controller,
            condition: Triggers.OnStepBegin(controller, StepStateType.Upkeep),
            effects: new IEffect[] { effect },
            // CR 603.4 — intervening-if: only put the echo trigger on the stack
            // at the FIRST qualifying upkeep, while it is unpaid + on the
            // battlefield.
            interveningIf: () => echoUnpaid && source.Zone == ZoneType.Battlefield,
            activeZones: new[] { ZoneType.Battlefield });
    }
}
