using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Lifecycle binder for Blood Moon's Layer 4 type-changing effect.
///
/// CR 305.6 / 613.1d — "Nonbasic lands are Mountains." While the source
/// Blood Moon is on the battlefield, a <see cref="SetSubtypesEffect"/> is
/// registered on the supplied <see cref="ContinuousEffectsService"/>
/// scoped to every nonbasic Land on the battlefield, replacing each such
/// land's land-subtype set with {Mountain}. Combined with PR #155's
/// <see cref="EffectiveManaAbilities"/>, affected lands lose their printed
/// mana abilities and tap for {R}.
///
/// The lifecycle mirrors <see cref="TorporOrbStaticEffect"/>: subscribe to
/// <see cref="CardMovedEvent"/>, register the effect when the source first
/// crosses onto the battlefield, unregister when it leaves. The Layer 4
/// effect itself is the single registered instance — the
/// <see cref="ContinuousEffect"/> base already short-circuits via
/// <see cref="ContinuousEffect.IsActive"/> when the source isn't on the
/// battlefield, so leaving the effect registered through a transient zone
/// flicker would still be safe, but explicit unregister keeps the
/// effects list tidy and lets <see cref="ContinuousEffectsService.Prune"/>
/// stay a noop here.
///
/// Design intentionally omits an <see cref="Majik.Core.Abilities.IStaticAbility"/>
/// wrapper: the layer system already executes Blood Moon's effect on
/// every <see cref="ContinuousEffectsService.Compute(Permanent)"/> call,
/// so we don't need <c>StaticAbilityManager.ApplyStaticAbilities</c> to
/// poll us. This matches the pattern Torpor Orb uses.
/// </summary>
public sealed class BloodMoonStaticEffect
{
    private static readonly IReadOnlySet<CardSubtype> MountainOnly =
        new HashSet<CardSubtype> { CardSubtype.Mountain };

    private readonly Permanent _source;
    private readonly ContinuousEffectsService _effects;
    private readonly IEventBus? _eventBus;
    private readonly Action<GameEvent> _handler;
    private SetSubtypesEffect? _registered;
    private bool _attached;

    public BloodMoonStaticEffect(
        Permanent source,
        ContinuousEffectsService effects,
        IEventBus? eventBus = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _eventBus = eventBus;
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
                // Scope: every nonbasic Land on the battlefield. The Layer 4
                // effect itself re-checks the battlefield zone via
                // AppliesTo; this predicate enforces the "nonbasic land"
                // half of CR 305.6.
                scope: p => p is Land && !p.HasSupertype(CardSupertype.Basic),
                category: LandSubtypes.All,
                newSubtypes: MountainOnly);
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
