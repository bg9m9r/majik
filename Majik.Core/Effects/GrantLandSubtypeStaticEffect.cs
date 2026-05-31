using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for Layer 4 subtype-adding static effects
/// that operate over a predicate-defined set of permanents.
///
/// CR 305.7 / 613.1d — a family of cards say "Each [some set of]
/// permanent is a [subtype] in addition to its other types." Examples:
///   * Urborg, Tomb of Yawgmoth — "Each land is a Swamp in addition to
///     its other types."
///   * Yavimaya, Cradle of Growth — "Each land is a Forest in addition
///     to its other types."
///
/// Both share the exact same machinery: while the source permanent is on
/// the battlefield, an <see cref="AddSubtypeToPermanentsEffect"/> is
/// registered on the supplied <see cref="ContinuousEffectsService"/>,
/// scoped to a caller-supplied predicate, with a single grantable
/// subtype. When the source leaves the battlefield, the effect is
/// unregistered. Combined with PR #155's
/// <see cref="EffectiveManaAbilities"/> additive-vs-replacement
/// detection, affected lands keep their printed mana abilities AND gain
/// the appropriate basic mana ability for the granted land subtype
/// (CR 305.6 / 305.7).
///
/// The lifecycle mirrors <see cref="RetypeLandsStaticEffect"/>: subscribe
/// to <see cref="CardMovedEvent"/>, register on ETB, unregister on LTB.
/// </summary>
public sealed class GrantLandSubtypeStaticEffect
{
    private readonly Permanent _source;
    private readonly ContinuousEffectsService _effects;
    private readonly IEventBus? _eventBus;
    private readonly Func<Permanent, bool> _scope;
    private readonly CardSubtype _subtype;
    private readonly Action<CardMovedEvent> _handler;
    private AddSubtypeToPermanentsEffect? _registered;
    private bool _attached;

    /// <summary>
    /// Build a lifecycle binder for a Layer 4 subtype-granting effect.
    /// </summary>
    /// <param name="source">The permanent whose presence on the
    /// battlefield gates the effect.</param>
    /// <param name="layers">The continuous-effects service the
    /// <see cref="AddSubtypeToPermanentsEffect"/> will be registered
    /// against.</param>
    /// <param name="eventBus">The bus to subscribe for
    /// <see cref="CardMovedEvent"/>. May be null — the effect will still
    /// activate on <see cref="Attach"/> if <paramref name="source"/> is
    /// already on the battlefield, but no zone-change tracking happens.</param>
    /// <param name="scope">Predicate selecting which permanents the
    /// subtype-granting effect applies to (e.g. <c>p =&gt; p is Land</c>
    /// for Urborg / Yavimaya).</param>
    /// <param name="subtypeToGrant">The single subtype that every matched
    /// permanent should additionally have. The grant is additive —
    /// existing subtypes are preserved.</param>
    public GrantLandSubtypeStaticEffect(
        Permanent source,
        ContinuousEffectsService layers,
        IEventBus? eventBus,
        Func<Permanent, bool> scope,
        CardSubtype subtypeToGrant)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _effects = layers ?? throw new ArgumentNullException(nameof(layers));
        _eventBus = eventBus;
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _subtype = subtypeToGrant;
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
        _eventBus?.Subscribe(_handler);
        Sync();
    }

    /// <summary>
    /// Unsubscribe and unregister the Layer 4 effect. Idempotent.
    /// </summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.Unsubscribe(_handler);
        Unregister();
    }

    private void OnEvent(CardMovedEvent e)
    {
        var moved = e;
        if (!ReferenceEquals(moved.Card, _source)) return;
        Sync();
    }

    private void Sync()
    {
        var shouldBeActive = _source.Zone == ZoneType.Battlefield;
        if (shouldBeActive && _registered == null)
        {
            _registered = new AddSubtypeToPermanentsEffect(
                _source,
                scope: _scope,
                subtype: _subtype);
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
