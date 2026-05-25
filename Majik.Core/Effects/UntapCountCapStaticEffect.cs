using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for an "untap at most <c>N</c> permanents
/// matching <c>filter</c> per untap step" static (CR 502.1 — Static Orb,
/// Winter Orb, Smoke).
///
/// While the source permanent is on the battlefield, a count-cap entry is
/// registered with <see cref="UntapStepRestrictions"/>. The cap fires
/// during the untap step thinning pass (see
/// <see cref="UntapStepRestrictions.ApplyCountCaps"/>), allowing at most
/// <see cref="UntapStepRestrictions.UntapCountCap.MaxCount"/> filter-
/// matching candidates per player.
///
/// Conditional caps ("as long as <c>source</c> is untapped" — Static Orb,
/// Winter Orb) supply an <c>isActive</c> predicate that re-checks the
/// source's live tap state at consultation time. Unconditional caps
/// (Smoke) pass <c>() => true</c>.
///
/// Lifecycle mirrors <see cref="DoesNotUntapStaticEffect"/>:
/// <list type="bullet">
///   <item>Subscribe to <see cref="CardMovedEvent"/> on Attach.</item>
///   <item>Sync on Attach (active if the source is already on the battlefield).</item>
///   <item>On every relevant move event, re-sync.</item>
///   <item>Detach unsubscribes and removes the registration.</item>
/// </list>
/// </summary>
public sealed class UntapCountCapStaticEffect
{
    private readonly Permanent _source;
    private readonly int _maxCount;
    private readonly Func<Permanent, bool> _filter;
    private readonly Func<bool> _isActive;
    private readonly IEventBus? _eventBus;
    private readonly Action<GameEvent> _handler;
    private readonly object _token = new();
    private bool _attached;
    private bool _registered;

    public UntapCountCapStaticEffect(
        Permanent source,
        int maxCount,
        Func<Permanent, bool> filter,
        Func<bool> isActive,
        IEventBus? eventBus)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (maxCount < 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
        _maxCount = maxCount;
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _isActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
        _eventBus = eventBus;
        _handler = OnEvent;
    }

    /// <summary>Whether the cap is currently registered.</summary>
    public bool IsRegistered => _registered;

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
            UntapStepRestrictions.MarkUntapCountCap(_token, _maxCount, _filter, _isActive);
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
