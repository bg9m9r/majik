using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.2 / CR 514.2 — a TEMPORARY (end-of-turn) control-changing effect: the
/// Threaten / Act of Treason family ("Gain control of target creature until end
/// of turn").
///
/// <para>Unlike the permanent-only <see cref="ControlChangeEffect"/> (Mind
/// Control) — which is purely decorative, surfacing the new controller only
/// through <see cref="ContinuousEffectsService.EffectiveController"/> while the
/// combat / priority / targeting subsystems read <see cref="Permanent.Controller"/>
/// directly — this effect swaps the target's <i>actual</i>
/// <see cref="Permanent.Controller"/> on registration, so the gained creature
/// can attack, block, be tapped for its abilities, etc., exactly as the
/// controlling player's own. The prior controller is snapshotted at
/// registration and restored by <see cref="OnExpired"/>, which the service fires
/// when the effect is dropped during the cleanup step (CR 514.2 — until-end-of-turn
/// effects end), inactive-effect pruning, or unregister.</para>
///
/// <para><b>Threaten template (CR 805 standard wording)</b> — the spell that
/// registers this effect is responsible for the surrounding rider: it also
/// untaps the creature and grants it haste until end of turn (a
/// <see cref="GrantKeywordUntilEndOfTurnEffect"/> with keyword "Haste"). The
/// haste is mandatory because a creature whose control changed this turn has
/// summoning sickness (CR 302.6) and could not otherwise attack the turn it is
/// stolen; granting haste lets it attack immediately. This effect itself owns
/// only the control swap + its reversion so the primitive stays composable.</para>
///
/// <para>This effect is a Layer-2 control entry (mirrors
/// <see cref="ControlChangeEffect.Layer"/>) but performs no working-set
/// mutation in <see cref="Apply(CreatureCharacteristics)"/> — the swap is a
/// direct controller change, not a computed override — and it does not derive
/// from <see cref="ControlChangeEffect"/>, so it is deliberately invisible to
/// <see cref="ContinuousEffectsService.EffectiveController"/> (which would
/// otherwise double-report against the already-swapped real controller).</para>
///
/// <para><b>"For as long as &lt;condition&gt;" duration (CR 611.2b / 613.2)</b>
/// — pass a non-null <paramref name="until"/> predicate to model the persistent
/// steal family ("gain control of target creature <i>for as long as you control
/// this</i>" / "<i>for as long as this remains on the battlefield</i>" — Sower
/// of Temptation, Mind Control–style Auras, Dragonlord Silumgar). When supplied,
/// the effect does NOT expire at the cleanup step
/// (<see cref="ExpiresAtEndOfTurn"/> becomes <c>false</c>); instead its
/// <see cref="IsActive"/> stays true only while BOTH the target is on the
/// battlefield AND the condition still holds. When the condition lapses
/// (e.g. the controlling source leaves play), <see cref="IsActive"/> goes false
/// and the service's <see cref="ContinuousEffectsService.Prune"/> drops it,
/// firing <see cref="OnExpired"/> to restore the prior controller. Pruning runs
/// as part of the state-based-action sweep (CR 704.3) and on every layer
/// recompute, so the revert lands the next time the game checks state after the
/// condition becomes false — the SBA-style condition check the pay-down sketch
/// calls for. With a null <paramref name="until"/> the effect keeps its legacy
/// until-end-of-turn (Threaten) semantics.</para>
/// </summary>
public sealed class TemporaryControlChangeEffect : ContinuousEffect
{
    private readonly Player _priorController;
    private readonly Func<bool>? _until;
    private bool _reverted;

    /// <summary>The permanent whose control was temporarily gained.</summary>
    public Permanent Target { get; }

    /// <summary>The player who has gained control until end of turn.</summary>
    public Player NewController { get; }

    /// <summary>
    /// Construct and immediately apply the control swap. The target's current
    /// controller is snapshotted as the revert destination, then its
    /// <see cref="Permanent.Controller"/> is set to <paramref name="newController"/>.
    /// </summary>
    /// <param name="target">The permanent whose control is gained.</param>
    /// <param name="newController">The player gaining control.</param>
    /// <param name="until">
    /// Optional "for as long as &lt;condition&gt;" predicate (CR 611.2b). When
    /// non-null the steal persists past end of turn and reverts when the
    /// predicate returns <c>false</c> (e.g.
    /// <c>() =&gt; source.Zone == ZoneType.Battlefield</c> for "for as long as
    /// this remains on the battlefield"). When null the steal is the legacy
    /// until-end-of-turn (Threaten) duration.
    /// </param>
    public TemporaryControlChangeEffect(Permanent target, Player newController, Func<bool>? until = null)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        NewController = newController ?? throw new ArgumentNullException(nameof(newController));
        _until = until;

        // CR 608.2g — snapshot the controller to restore at cleanup. Fall back
        // to the owner if (defensively) no controller is set.
        _priorController = target.Controller ?? target.Owner!;

        // CR 110.2 / 613.2 — actually change control. The combat validator,
        // priority manager and targeting all read Permanent.Controller, so the
        // gained creature behaves as the new controller's for every subsystem.
        target.ChangeController(newController);
    }

    public override Layer Layer => Layer.Control;
    public override bool AppliesTo(Creature c) => false; // not P/T-mutating

    /// <summary>
    /// CR 514.2 — a null-condition (Threaten) steal expires at the cleanup step.
    /// A "for as long as &lt;condition&gt;" steal (CR 611.2b) does NOT: it lives
    /// until its <see cref="_until"/> condition lapses, surfaced through
    /// <see cref="IsActive"/> + <see cref="ContinuousEffectsService.Prune"/>.
    /// </summary>
    public override bool ExpiresAtEndOfTurn => _until is null;

    /// <summary>
    /// CR 514.2 / CR 611.2b — active while the target is on the battlefield AND
    /// (for a "for as long as" steal) the duration condition still holds. When
    /// either fails, the service's <see cref="ContinuousEffectsService.Prune"/>
    /// drops it (firing <see cref="OnExpired"/>); restoring control on a
    /// permanent that has left play is a harmless no-op.
    /// </summary>
    public override bool IsActive() =>
        Target.Zone == ZoneType.Battlefield && (_until is null || _until());

    public override void Apply(CreatureCharacteristics chars) { /* no-op */ }

    /// <summary>
    /// CR 514.2 — restore the snapshotted prior controller when the effect's
    /// duration ends. Idempotent: a second call (e.g. unregister after the
    /// cleanup-step expiry) is a no-op.
    /// </summary>
    public override void OnExpired()
    {
        if (_reverted) return;
        _reverted = true;

        // Only restore if we still hold the swap — if some later effect changed
        // control again, that newer effect owns the controller and we must not
        // clobber it.
        if (ReferenceEquals(Target.Controller, NewController))
        {
            Target.ChangeController(_priorController);
        }
    }
}
