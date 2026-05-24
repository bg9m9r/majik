using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.1f — Layer 6 ability-adding continuous effect. Grants a full
/// <see cref="IAbility"/> instance (triggered, activated, static, or
/// non-keyword marker like <see cref="ProtectionAbility"/>) to a target
/// <see cref="Permanent"/> while the effect is active.
///
/// Lifecycle is keyed by the effect's <see cref="Source"/> permanent and a
/// <c>targetSelector</c> read at sync time:
///   - on <see cref="Sync"/>, if the source is on the battlefield and the
///     selector returns a target, the granted ability is attached to that
///     target via <see cref="Card.AddAbility"/>;
///   - if the target changes (e.g. an equipment re-equips), the previous
///     grant is revoked and the new bearer receives a fresh instance;
///   - if the source leaves play OR the selector returns null OR the
///     effect is unregistered, the grant is revoked (CR 613.6e — when the
///     ability-granting effect ends, the granted ability is lost).
///
/// The granted ability is produced by <c>abilityFactory(target)</c> on each
/// (re-)grant, so closures (e.g. trigger handlers that capture the bearer)
/// bind to the live instance.
///
/// Composition with <see cref="LoseAllAbilitiesEffect"/> (CR 613.6 / 613.8):
///   - <see cref="ContinuousEffectsService"/> suppresses any continuous
///     effect whose <see cref="Source"/> creature has been stripped by an
///     active Humility-class effect; <see cref="GrantAbilityEffect"/>
///     participates by overriding <see cref="Source"/>.
///   - When the grant TARGET (not source) has been stripped, the granted
///     ability is removed by the service via <see cref="Revoke"/> during the
///     same <see cref="Apply"/> pass.
///
/// This effect does not write to <see cref="CreatureCharacteristics"/> in
/// the layer pass — the ability is materialised on the bearer's
/// <see cref="Card.Abilities"/> list, which is what downstream consumers
/// (trigger manager, target legality, protection helpers) read.
/// </summary>
public sealed class GrantAbilityEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly Func<Permanent?> _targetSelector;
    private readonly Func<Permanent, IAbility> _abilityFactory;
    private readonly bool _expiresAtEndOfTurn;

    private Permanent? _grantedTo;
    private IAbility? _grantedAbility;

    /// <param name="source">
    /// CR 613.1g — the permanent generating the effect. The grant is alive
    /// only while <paramref name="source"/> is on the battlefield.
    /// </param>
    /// <param name="targetSelector">
    /// Resolves the current target each time <see cref="Sync"/> runs. Lets
    /// the caller key off live state (e.g. <see cref="Permanent.AttachedTo"/>
    /// for equipment, a captured token reference for copy effects).
    /// Returning null revokes any active grant.
    /// </param>
    /// <param name="abilityFactory">
    /// Builds a fresh <see cref="IAbility"/> instance for the bearer on each
    /// grant. The bearer is passed so trigger / activated closures capture
    /// the live target.
    /// </param>
    /// <param name="expiresAtEndOfTurn">
    /// CR 514.2 — when true, the effect drops in the cleanup step (e.g.
    /// "target creature gains flying until end of turn").
    /// </param>
    public GrantAbilityEffect(
        Permanent source,
        Func<Permanent?> targetSelector,
        Func<Permanent, IAbility> abilityFactory,
        bool expiresAtEndOfTurn = false)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _targetSelector = targetSelector ?? throw new ArgumentNullException(nameof(targetSelector));
        _abilityFactory = abilityFactory ?? throw new ArgumentNullException(nameof(abilityFactory));
        _expiresAtEndOfTurn = expiresAtEndOfTurn;
    }

    /// <summary>
    /// Convenience overload for the "grant a fixed ability to a fixed target"
    /// shape (e.g. Sword of Fire and Ice projecting protection markers onto
    /// the equipped creature — the target is dynamic but the ability is the
    /// same per slot).
    /// </summary>
    public GrantAbilityEffect(
        Permanent source,
        Permanent target,
        IAbility ability,
        bool expiresAtEndOfTurn = false)
        : this(
            source,
            () => target,
            _ => ability,
            expiresAtEndOfTurn)
    {
    }

    public override Layer Layer => Layer.Abilities;

    public override Permanent? Source => _source;

    public override bool ExpiresAtEndOfTurn => _expiresAtEndOfTurn;

    /// <summary>The currently-granted ability, or null when no grant is live.</summary>
    public IAbility? GrantedAbility => _grantedAbility;

    /// <summary>The bearer of the current grant, or null when revoked.</summary>
    public Permanent? GrantedTo => _grantedTo;

    public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

    /// <summary>
    /// CR 613 — the effect "applies to" its current grant target. The layer
    /// pass calls <see cref="Apply"/> at most once per target per Compute, so
    /// we use that hook to (re-)sync the grant lifecycle.
    /// </summary>
    public override bool AppliesTo(Permanent permanent)
    {
        var desired = ResolveTarget();
        return desired != null && ReferenceEquals(permanent, desired);
    }

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    /// <summary>
    /// Per CR 613 layer pass — re-sync the grant when this effect's slot
    /// applies to <paramref name="chars"/>'s underlying permanent. The
    /// effect doesn't mutate <see cref="CreatureCharacteristics"/> directly;
    /// the granted ability sits on the bearer's
    /// <see cref="Card.Abilities"/> list.
    /// </summary>
    public override void Apply(PermanentCharacteristics chars)
    {
        // The actual lifecycle sync is driven by callers (factories) via
        // Sync(), and by ContinuousEffectsService at compute time via
        // SyncFromService. This Apply hook is intentionally a no-op — see
        // ContinuousEffectsService.Compute which calls Sync explicitly so the
        // grant lifecycle stays consistent regardless of which permanent the
        // service is computing for at the moment.
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        // No-op — see Apply(PermanentCharacteristics).
    }

    /// <summary>
    /// Reconcile the live grant with the current selector + source state.
    /// Called by <see cref="ContinuousEffectsService.Compute"/> on every
    /// pass, and may also be called directly by factories that change the
    /// effect's target (e.g. an equipment re-equips outside a layer pass).
    /// </summary>
    public void Sync()
    {
        var desired = IsActive() ? ResolveTarget() : null;

        if (ReferenceEquals(desired, _grantedTo))
        {
            return; // already matches
        }

        Revoke();

        if (desired != null)
        {
            _grantedAbility = _abilityFactory(desired);
            desired.AddAbility(_grantedAbility);
            _grantedTo = desired;
        }
    }

    /// <summary>
    /// Revoke any live grant. Idempotent. Called when the source leaves
    /// play, the selector returns null, or the effect is unregistered from
    /// the service (the service calls this during <see cref="ContinuousEffectsService.Unregister"/>
    /// + <see cref="ContinuousEffectsService.Prune"/>).
    /// </summary>
    public void Revoke()
    {
        if (_grantedAbility == null || _grantedTo == null) return;
        _grantedTo.RemoveAbility(_grantedAbility);
        _grantedAbility = null;
        _grantedTo = null;
    }

    private Permanent? ResolveTarget()
    {
        var t = _targetSelector();
        if (t == null) return null;
        if (t.Zone != ZoneType.Battlefield) return null;
        return t;
    }
}
