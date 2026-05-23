using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 303.4 / 305.6 / 613.1d — Layer 4 static effect for a land-targeting
/// Aura that retypes its single attachment target.
///
/// Family: Spreading Seas, Sea's Claim — "Enchanted land is an Island."
/// While the aura is on the battlefield, register a
/// <see cref="SetSubtypesEffect"/> whose scope is exactly the permanent
/// the aura is attached to. When the aura leaves the battlefield, the
/// layer effect unregisters. CR 305.6 (PR #155's
/// <see cref="EffectiveManaAbilities"/>) then derives the new basic-mana
/// ability automatically.
///
/// Lifecycle mirrors <see cref="RetypeLandsStaticEffect"/>: subscribe to
/// <see cref="CardMovedEvent"/>, register/unregister on ETB/LTB. The
/// difference is the scope predicate: instead of a static "every nonbasic
/// land" check, the predicate reads <see cref="Permanent.AttachedTo"/> at
/// call time, so it follows the aura's attachment slot dynamically.
///
/// Detach handling: there is no separate detach event today. If
/// <see cref="Permanent.AttachedTo"/> becomes null (e.g. the bearer
/// leaves the battlefield and <see cref="AttachmentLegalityCheck"/>
/// detaches the aura before moving it to graveyard), the scope predicate
/// simply matches nothing and the effect is inert — the aura itself
/// LTB-ing will then fire the unregister via the
/// <see cref="CardMovedEvent"/> hook.
/// </summary>
public sealed class AttachedAuraRetypeStaticEffect
{
    private readonly Permanent _source;
    private readonly ContinuousEffectsService _effects;
    private readonly IEventBus? _eventBus;
    private readonly IReadOnlySet<CardSubtype> _newSubtypes;
    private readonly Action<GameEvent> _handler;
    private SetSubtypesEffect? _registered;
    private bool _attached;

    /// <summary>
    /// Build a lifecycle binder for a land-Aura Layer 4 retype effect.
    /// </summary>
    /// <param name="auraSource">The Aura permanent whose presence on the
    /// battlefield gates the effect, and whose
    /// <see cref="Permanent.AttachedTo"/> identifies the single target the
    /// retype applies to.</param>
    /// <param name="layers">The continuous-effects service the
    /// <see cref="SetSubtypesEffect"/> will be registered against.</param>
    /// <param name="eventBus">The bus to subscribe for
    /// <see cref="CardMovedEvent"/>. May be null — the effect will still
    /// activate on <see cref="Attach"/> if the aura is already on the
    /// battlefield, but no zone-change tracking happens after that.</param>
    /// <param name="newLandSubtypes">The land subtype(s) the attached
    /// permanent should end up with — usually a single value like
    /// <c>{ Island }</c>. Category is implicitly
    /// <see cref="LandSubtypes.All"/> (this family rewrites the land-subtype
    /// slot only).</param>
    public AttachedAuraRetypeStaticEffect(
        Permanent auraSource,
        ContinuousEffectsService layers,
        IEventBus? eventBus,
        IReadOnlySet<CardSubtype> newLandSubtypes)
    {
        _source = auraSource ?? throw new ArgumentNullException(nameof(auraSource));
        _effects = layers ?? throw new ArgumentNullException(nameof(layers));
        _eventBus = eventBus;
        _newSubtypes = newLandSubtypes ?? throw new ArgumentNullException(nameof(newLandSubtypes));
        _handler = OnEvent;
    }

    /// <summary>Whether the Layer 4 effect is currently registered.</summary>
    public bool IsActive => _registered != null;

    /// <summary>
    /// Subscribe to zone-move events and register the Layer 4 effect if
    /// the aura is already on the battlefield at attach time. Idempotent.
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
            // The scope predicate reads _source.AttachedTo at call time —
            // SetSubtypesEffect.AppliesTo evaluates it on every layer
            // computation, so it tracks the aura's attachment slot
            // dynamically. If AttachedTo is null (aura just entered, not
            // yet attached, or its bearer LTB'd before this aura did),
            // the predicate returns false and no permanent is affected.
            _registered = new SetSubtypesEffect(
                _source,
                scope: p => _source.AttachedTo != null
                            && ReferenceEquals(p, _source.AttachedTo),
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
