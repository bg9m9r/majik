using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for Layer 4 land-retype static effects.
///
/// CR 305.6 / 613.1d — a family of cards say "[some set of] lands are
/// [basic land type]." Examples:
///   * Blood Moon / Magus of the Moon — "Nonbasic lands are Mountains."
///   * Harbinger of the Seas — "Nonbasic lands are Islands."
///   * Conversion — "All Mountains are Plains."
///
/// All four share the exact same machinery: while the source permanent
/// is on the battlefield, a <see cref="SetSubtypesEffect"/> is registered
/// on the supplied <see cref="ContinuousEffectsService"/>, scoped to a
/// caller-supplied predicate, with category <see cref="LandSubtypes.All"/>
/// (every retyper in this family rewrites the land-subtype slot only) and
/// the caller-supplied <c>newLandSubtypes</c>. When the source leaves the
/// battlefield, the effect is unregistered. Combined with PR #155's
/// <see cref="EffectiveManaAbilities"/>, affected lands lose their printed
/// mana abilities and tap for the new basic-land color (CR 305.6).
///
/// The lifecycle mirrors <see cref="TorporOrbStaticEffect"/>: subscribe
/// to <see cref="CardMovedEvent"/>, register on ETB, unregister on LTB.
/// The Layer 4 effect itself is the single registered instance — the
/// <see cref="ContinuousEffect"/> base already short-circuits via
/// <see cref="ContinuousEffect.IsActive"/> when the source isn't on the
/// battlefield, so leaving the effect registered through a transient zone
/// flicker would still be safe, but explicit unregister keeps the
/// effects list tidy and lets <see cref="ContinuousEffectsService.Prune"/>
/// stay a noop here.
///
/// Design intentionally omits an <see cref="Majik.Core.Abilities.IStaticAbility"/>
/// wrapper: the layer system already executes the effect on every
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> call, so we
/// don't need <c>StaticAbilityManager.ApplyStaticAbilities</c> to poll us.
/// </summary>
public sealed class RetypeLandsStaticEffect
{
    private readonly Permanent _source;
    private readonly ContinuousEffectsService _effects;
    private readonly IEventBus? _eventBus;
    private readonly Func<Permanent, bool> _scope;
    private readonly IReadOnlySet<CardSubtype> _newSubtypes;
    private readonly Action<GameEvent> _handler;
    private SetSubtypesEffect? _registered;
    private bool _attached;

    /// <summary>
    /// Build a lifecycle binder for a Layer 4 land-retype effect.
    /// </summary>
    /// <param name="source">The permanent whose presence on the
    /// battlefield gates the effect.</param>
    /// <param name="layers">The continuous-effects service the
    /// <see cref="SetSubtypesEffect"/> will be registered against.</param>
    /// <param name="eventBus">The bus to subscribe for
    /// <see cref="CardMovedEvent"/>. May be null — the effect will still
    /// activate on <see cref="Attach"/> if <paramref name="source"/> is
    /// already on the battlefield, but no zone-change tracking happens.</param>
    /// <param name="scope">Predicate selecting which permanents the
    /// type-changing effect applies to (e.g. <c>p =&gt; p is Land &amp;&amp;
    /// !p.HasSupertype(CardSupertype.Basic)</c> for Blood Moon).</param>
    /// <param name="newLandSubtypes">The land subtype(s) every matched
    /// permanent should end up with — usually a single value like
    /// <c>{ Mountain }</c>. The category is implicitly
    /// <see cref="LandSubtypes.All"/>: every member of this family rewrites
    /// the land-subtype slot only, so non-land subtypes (creature types on
    /// a Dryad Arbor, etc.) are left untouched.</param>
    public RetypeLandsStaticEffect(
        Permanent source,
        ContinuousEffectsService layers,
        IEventBus? eventBus,
        Func<Permanent, bool> scope,
        IReadOnlySet<CardSubtype> newLandSubtypes)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _effects = layers ?? throw new ArgumentNullException(nameof(layers));
        _eventBus = eventBus;
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _newSubtypes = newLandSubtypes ?? throw new ArgumentNullException(nameof(newLandSubtypes));
        _handler = OnEvent;
    }

    /// <summary>Whether the Layer 4 effect is currently registered.</summary>
    public bool IsActive => _registered != null;

    /// <summary>
    /// Subscribe to zone-move events and register the Layer 4 effect if
    /// the source is already on the battlefield at attach time. Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus?.SubscribeAll(_handler);
        Sync();
    }

    /// <summary>
    /// Unsubscribe and unregister the Layer 4 effect. Idempotent.
    /// </summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.UnsubscribeAll(_handler);
        Unregister();
    }

    private void OnEvent(GameEvent e)
    {
        if (e is not CardMovedEvent moved) return;
        if (!ReferenceEquals(moved.Card, _source)) return;
        Sync();
    }

    private void Sync()
    {
        var shouldBeActive = _source.Zone == ZoneType.Battlefield;
        if (shouldBeActive && _registered == null)
        {
            _registered = new SetSubtypesEffect(
                _source,
                scope: _scope,
                category: LandSubtypes.All,
                newSubtypes: _newSubtypes);
            _effects.Register(_registered);
        }
        else if (!shouldBeActive)
        {
            Unregister();
        }
    }

    private void Unregister()
    {
        if (_registered == null) return;
        _effects.Unregister(_registered);
        _registered = null;
    }
}
