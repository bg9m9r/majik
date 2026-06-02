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
/// </summary>
public sealed class TemporaryControlChangeEffect : ContinuousEffect
{
    private readonly Player _priorController;
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
    public TemporaryControlChangeEffect(Permanent target, Player newController)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        NewController = newController ?? throw new ArgumentNullException(nameof(newController));

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
    public override bool ExpiresAtEndOfTurn => true;

    /// <summary>
    /// CR 514.2 — active while the target is on the battlefield. If the target
    /// leaves play before cleanup, the service's <see cref="ContinuousEffectsService.Prune"/>
    /// drops it (firing <see cref="OnExpired"/>); restoring control on a
    /// permanent that has left play is a harmless no-op.
    /// </summary>
    public override bool IsActive() => Target.Zone == ZoneType.Battlefield;

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
