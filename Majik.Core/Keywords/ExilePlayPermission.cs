using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 118.9 / CR 514.2 — the duration after which a temporary "you may
/// play/cast this exiled card" permission expires and the engine revokes it.
///
/// <para>
/// A grant stamped by an impulse-draw family effect (Reckless Impulse, Light
/// Up the Stage, March of Reckless Joy, Harnfel's "you may play those cards
/// this turn", Ragavan, …) is a TEMPORARY play permission bounded by a
/// duration. The engine must REVOKE the cast/play authorization when that
/// window closes rather than let the per-card
/// <see cref="Card.RuntimeExileCastAllowedCaster"/> stamp linger past its
/// duration — otherwise a card exiled "until end of turn" stays castable on
/// later turns.
/// </para>
/// </summary>
public enum ExilePlayExpiry
{
    /// <summary>
    /// "Until end of turn" / "this turn" (CR 514.2) — the grant clears at the
    /// FIRST Cleanup step belonging to the granted caster after it is stamped.
    /// Harnfel ("you may play those cards this turn"), Ragavan, Light Up the
    /// Stage's reminder corner, any "this turn"-scoped impulse.
    /// </summary>
    EndOfTurn,

    /// <summary>
    /// "Until the end of your next turn" (CR 514.2) — the grant clears at the
    /// SECOND Cleanup step belonging to the granted caster after it is stamped.
    /// A sorcery-speed impulse cast on the caster's own turn (Reckless Impulse,
    /// Light Up the Stage, March of Reckless Joy) resolves during the caster's
    /// turn, so the first such Cleanup is THIS turn's (grant survives) and the
    /// second is the caster's NEXT turn's (grant clears).
    /// </summary>
    EndOfYourNextTurn,
}

/// <summary>
/// CR 118.9 — reusable "exile this card and grant a TEMPORARY play/cast
/// permission with an expiry moment" primitive.
///
/// <para>
/// Generalizes the EOT-expiry bookkeeping that was previously copy-pasted
/// inline into every impulse-draw factory (Reckless Impulse, Light Up the
/// Stage, March of Reckless Joy, …): each stamped the per-card runtime grant
/// (<see cref="Card.GrantRuntimeExileCast"/>) and then duplicated a
/// <see cref="StepStartedEvent"/> subscription that counted Cleanup steps
/// belonging to the caster to know WHEN to revoke. The counting was identical
/// in every factory and exactly the residual the "temporary-play-this-card
/// permission-expiry" deferral named: a granted play permission bounded by a
/// duration so the engine revokes the authorization when the window closes.
/// </para>
///
/// <para>
/// This helper centralizes the stamp + the expiry into a single declarative
/// call. <see cref="GrantUntil"/> stamps the runtime grant on the exiled card
/// and — when a live <see cref="IEventBus"/> is supplied — schedules its
/// revocation at the declared <see cref="ExilePlayExpiry"/> moment by counting
/// Cleanup steps owned by the granted caster (CR 514.2). Without a bus the
/// grant persists until the caller clears it (the test path), exactly matching
/// the pre-existing per-factory behaviour.
/// </para>
///
/// <para>
/// The cast/play path itself is unchanged: <see cref="Costs.ExileCastAlternativeCost"/>
/// reads the same per-card stamp this helper writes, and
/// <see cref="Majik.Core.Game.SpellCastFlow"/> already routes a card from the
/// Exile zone onto the stack under that alt-cost — so the registry consult the
/// engine already does as a cast source is reused verbatim. The only thing
/// this primitive adds is the centralized, declarative EXPIRY of that
/// permission.
/// </para>
/// </summary>
public static class ExilePlayPermission
{
    /// <summary>
    /// Stamp a temporary play/cast permission on <paramref name="exiledCard"/>
    /// for <paramref name="caster"/> at <paramref name="cost"/>, and — when
    /// <paramref name="eventBus"/> is non-null — schedule its revocation at the
    /// <paramref name="expiry"/> moment (CR 118.9 / 514.2).
    ///
    /// <para>
    /// The grant is the per-card runtime stamp
    /// (<see cref="Card.GrantRuntimeExileCast"/>) that
    /// <see cref="Costs.ExileCastAlternativeCost"/> consults — so the card
    /// becomes a legal cast source from exile for exactly <paramref name="caster"/>
    /// until the window closes. <paramref name="spendAsAnyColor"/> (CR 609.4b)
    /// optionally relaxes the cost's coloured pips (Robber of the Rich-style).
    /// </para>
    ///
    /// <para>
    /// Returns the <see cref="Action"/> the scheduled handler runs to revoke
    /// the grant (it clears the per-card stamp). Callers that want to revoke
    /// early — or that have no bus — may invoke or store it; with a bus it also
    /// fires automatically at the expiry moment and unsubscribes.
    /// </para>
    /// </summary>
    public static Action GrantUntil(
        Card exiledCard,
        Player caster,
        ManaCost cost,
        ExilePlayExpiry expiry,
        IEventBus? eventBus = null,
        bool spendAsAnyColor = false)
    {
        ArgumentNullException.ThrowIfNull(exiledCard);
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(cost);

        exiledCard.GrantRuntimeExileCast(caster, cost, spendAsAnyColor);

        void Revoke() => exiledCard.ClearRuntimeExileCast();

        ScheduleRevocation(caster, expiry, eventBus, Revoke);
        return Revoke;
    }

    /// <summary>
    /// Schedule a single <paramref name="revoke"/> callback to run at the
    /// <paramref name="expiry"/> moment for <paramref name="caster"/> by
    /// counting Cleanup steps the caster owns (CR 514.2). No-op when
    /// <paramref name="eventBus"/> is null (test path — caller clears by hand).
    ///
    /// <para>
    /// Exposed for callers (e.g. Harnfel) that stamp several exiled cards under
    /// ONE duration and want a single shared subscription revoking all of them
    /// at once, rather than one subscription per card. The
    /// <paramref name="revoke"/> closure should clear every grant the caller
    /// stamped under this window.
    /// </para>
    /// </summary>
    public static void ScheduleRevocation(
        Player caster,
        ExilePlayExpiry expiry,
        IEventBus? eventBus,
        Action revoke)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(revoke);
        if (eventBus == null) return;

        // CR 514.2 — "until end of turn" clears at the FIRST cleanup the caster
        // owns; "until end of your next turn" at the SECOND (the first belongs
        // to the turn the grant was stamped on).
        var cleanupsNeeded = expiry == ExilePlayExpiry.EndOfTurn ? 1 : 2;
        var cleanupsSeen = 0;

        Action<StepStartedEvent>? handler = null;
        handler = e =>
        {
            if (e.StepType != PhaseStateType.Cleanup) return;
            if (!ReferenceEquals(e.Player, caster)) return;
            cleanupsSeen++;
            if (cleanupsSeen < cleanupsNeeded) return;

            revoke();
            if (handler != null) eventBus.Unsubscribe(handler);
        };
        eventBus.Subscribe(handler);
    }
}
