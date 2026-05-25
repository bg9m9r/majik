using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for a "this permanent doesn't untap during
/// its controller's untap step" self-targeted static (CR 502.1 — Mana
/// Vault, Stasis-style self-skip).
///
/// While the source permanent is on the battlefield, an entry is
/// registered with <see cref="UntapStepRestrictions"/> keyed by this
/// lifecycle instance. When the source leaves the battlefield, the entry
/// is removed.
///
/// Lifecycle mirrors <see cref="CastFromHandOnlyRestrictionEffect"/>:
/// <list type="bullet">
///   <item>Subscribe to <see cref="CardMovedEvent"/> on Attach.</item>
///   <item>Sync on Attach (active if the source is already on the
///         battlefield).</item>
///   <item>On every relevant move event, re-sync.</item>
///   <item>Detach unsubscribes and removes the registration.</item>
/// </list>
/// </summary>
public sealed class DoesNotUntapStaticEffect
{
    private readonly Permanent _source;
    private readonly IEventBus? _eventBus;
    private readonly Action<GameEvent> _handler;
    private readonly object _token = new();
    private bool _attached;
    private bool _registered;

    public DoesNotUntapStaticEffect(Permanent source, IEventBus? eventBus)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _eventBus = eventBus;
        _handler = OnEvent;
    }

    /// <summary>Whether the restriction is currently registered.</summary>
    public bool IsActive => _registered;

    /// <summary>Subscribe and register if source is on battlefield. Idempotent.</summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus?.SubscribeAll(_handler);
        Sync();
    }

    /// <summary>Unsubscribe and remove the registration. Idempotent.</summary>
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
        if (_source.Zone == ZoneType.Battlefield)
        {
            if (_registered) return;
            UntapStepRestrictions.MarkPermanentDoesNotUntap(_token, _source);
            _registered = true;
        }
        else
        {
            Unregister();
        }
    }

    private void Unregister()
    {
        if (!_registered) return;
        UntapStepRestrictions.RemoveAll(_token);
        _registered = false;
    }
}
