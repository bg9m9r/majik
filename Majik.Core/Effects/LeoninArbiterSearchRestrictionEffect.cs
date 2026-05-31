using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Marker lifecycle binder for Leonin Arbiter's search-tax static effect.
///
/// Oracle text: "Players can't search their libraries unless they pay {2}."
///
/// ## Implemented (v1 — structural shape only)
/// This class is registered on the <see cref="ContinuousEffectsService"/>
/// as a sentinel <see cref="ContinuousEffect"/> while Leonin Arbiter is on
/// the battlefield. The <em>enforcement</em> of the search restriction
/// (intercepting tutor / fetch land / Path to Exile search triggers) is
/// DEFERRED because the engine has no unified "search library" surface yet
/// — search effects are currently scattered across individual SpellDefinitions
/// and factory callbacks with no shared hook point.
///
/// When that surface lands, enforcement should:
///   1. Query <see cref="ContinuousEffectsService"/> for any registered
///      <see cref="LeoninArbiterSearchRestrictionEffect"/> that is still
///      active (i.e. <see cref="IsActive"/> returns true).
///   2. If one is found, require the searching player to pay {2} before
///      the search may proceed (CR 701.19 / CR 601.2b — additional cost
///      offered as an option; player who can't or won't pay cannot search).
///
/// The marker is an instance of <see cref="ContinuousEffect"/> so it
/// integrates naturally with the existing
/// <see cref="ContinuousEffectsService.Register"/> / Unregister lifecycle
/// and can be detected via
/// <c>_effects.OfType&lt;LeoninArbiterSearchRestrictionEffect&gt;()</c>
/// when the enforcement surface is ready.
///
/// ## Lifecycle
/// - Call <see cref="Attach"/> once after constructing the effect to
///   subscribe to <see cref="CardMovedEvent"/> and sync initial active state.
/// - Call <see cref="Detach"/> at game teardown to release the event
///   subscription and unregister from the effects service.
/// </summary>
public sealed class LeoninArbiterSearchRestrictionEffect : ContinuousEffect
{
    private readonly ICard _source;
    private readonly ContinuousEffectsService _effects;
    private readonly IEventBus? _eventBus;
    private readonly Action<CardMovedEvent> _handler;
    private bool _attached;
    private bool _currentlyActive;

    public LeoninArbiterSearchRestrictionEffect(
        ICard source,
        ContinuousEffectsService effects,
        IEventBus? eventBus = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _eventBus = eventBus;
        _handler = OnEvent;
    }

    /// <summary>
    /// Subscribe to zone-move events and register the restriction if Leonin
    /// Arbiter is already on the battlefield at attach time.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus?.Subscribe(_handler);
        SyncRegistration();
    }

    /// <summary>
    /// Unsubscribe from events and unregister the restriction effect.
    /// </summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.Unsubscribe(_handler);
        if (_currentlyActive)
        {
            _effects.Unregister(this);
            _currentlyActive = false;
        }
    }

    // CR 613 layer assignment: this is a structural marker that does not
    // mutate any layer characteristics. Layer 7 (P/T) is the last layer;
    // assigning it here avoids interference with earlier layers. The
    // restriction does not "apply" to any creature in the CR 613 sense —
    // it is a cross-permanent static that enforcement code will query
    // directly rather than route through the layer pipeline.
    // Assigned to PT_Modify (Layer 7c) as a convenient marker slot — this
    // effect never mutates P/T, it is purely structural. Any layer value
    // would work since AppliesTo always returns false.
    public override Layer Layer => Layer.PT_Modify;

    // This marker never applies to any specific permanent via the layer pipeline.
    public override bool AppliesTo(Creature creature) => false;

    // No-op: enforcement is handled outside the layer pipeline (deferred).
    public override void Apply(CreatureCharacteristics chars) { }

    /// <summary>
    /// True while the restriction is registered (Leonin Arbiter on the
    /// battlefield). Enforcement code should check this before requiring
    /// the search tax.
    /// </summary>
    public bool IsRestrictionActive => _currentlyActive;

    // ContinuousEffect.IsActive() is used by ContinuousEffectsService.Prune()
    // to garbage-collect stale effects. We manage our own registration via
    // SyncRegistration so the effect is only in the service while active —
    // therefore this can always return true (the service never sees a stale
    // entry from us).
    public override bool IsActive() => true;

    private void OnEvent(CardMovedEvent e)
    {
        var moved = e;
        if (!ReferenceEquals(moved.Card, _source)) return;
        SyncRegistration();
    }

    private void SyncRegistration()
    {
        var shouldBeActive = _source.Zone == ZoneType.Battlefield;

        if (shouldBeActive && !_currentlyActive)
        {
            _effects.Register(this);
            _currentlyActive = true;
        }
        else if (!shouldBeActive && _currentlyActive)
        {
            _effects.Unregister(this);
            _currentlyActive = false;
        }
    }
}
