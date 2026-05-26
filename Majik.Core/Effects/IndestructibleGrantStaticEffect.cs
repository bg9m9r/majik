using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for a "selected permanents have indestructible"
/// static effect (CR 117.1 / 702.12 / 613.1f). The canonical caller is
/// Darksteel Forge — "Other artifacts you control have indestructible." —
/// but the surface is generic: pass any <see cref="Func{ICard, Boolean}"/>
/// predicate and any source permanent.
///
/// While the source permanent is on the battlefield, the supplied predicate
/// is registered into <see cref="IndestructibleGrantRegistry"/>. When the
/// source leaves the battlefield, the grant is removed. Lifecycle mirrors
/// <see cref="FlashGrantStaticEffect"/>:
/// <list type="bullet">
///   <item>Subscribe to <see cref="CardMovedEvent"/> on Attach.</item>
///   <item>Sync on Attach (active if the source is already on the
///         battlefield).</item>
///   <item>On every relevant move event, re-sync.</item>
///   <item>Detach unsubscribes and removes the registration.</item>
/// </list>
///
/// The predicate is consulted at destroy-time (see
/// <see cref="Majik.Core.Combat.CombatAbilities.HasIndestructible"/> and
/// <see cref="Majik.Core.CardData.OracleSpellBinder.MoveToGraveyard"/>),
/// so it sees the card while it is on the battlefield. The predicate
/// should key off identity properties (controller, types, subtypes), not
/// zone.
/// </summary>
public sealed class IndestructibleGrantStaticEffect
{
    private readonly Permanent? _source;
    private readonly IEventBus? _eventBus;
    private readonly Func<ICard, bool> _predicate;
    private readonly Func<bool> _activeWhile;
    private readonly Action<GameEvent> _handler;
    private readonly object _token = new();
    private bool _attached;
    private bool _registered;

    /// <summary>
    /// Build an indestructible-grant lifecycle.
    /// </summary>
    /// <param name="source">The permanent gating the effect. May be null
    /// for emblem-style "always on" grants — pair with
    /// <paramref name="activeWhile"/> = <c>() =&gt; true</c>.</param>
    /// <param name="eventBus">Event bus for <see cref="CardMovedEvent"/>.
    /// May be null — Attach will still sync once.</param>
    /// <param name="predicate">Returns true for every card the grant
    /// should cover. Called at every destroy-time gate, so should be
    /// cheap and side-effect free.</param>
    /// <param name="activeWhile">Override the default "source is on the
    /// battlefield" gate. Defaults to <c>source.Zone == Battlefield</c>
    /// when <paramref name="source"/> is non-null, or <c>true</c>
    /// otherwise.</param>
    public IndestructibleGrantStaticEffect(
        Permanent? source,
        IEventBus? eventBus,
        Func<ICard, bool> predicate,
        Func<bool>? activeWhile = null)
    {
        _source = source;
        _eventBus = eventBus;
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        _activeWhile = activeWhile ?? DefaultActiveGate;
        _handler = OnEvent;
    }

    /// <summary>Whether the grant is currently registered.</summary>
    public bool IsActive => _registered;

    /// <summary>
    /// Subscribe to zone-move events and register if the source is
    /// already on the battlefield (or activeWhile() is true). Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus?.SubscribeAll(_handler);
        Sync();
    }

    /// <summary>
    /// Unsubscribe and remove the grant. Idempotent.
    /// </summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.UnsubscribeAll(_handler);
        Unregister();
    }

    private bool DefaultActiveGate()
    {
        if (_source == null) return true;
        return _source.Zone == ZoneType.Battlefield;
    }

    private void OnEvent(GameEvent e)
    {
        if (e is not CardMovedEvent moved) return;
        if (_source != null && !ReferenceEquals(moved.Card, _source)) return;
        Sync();
    }

    private void Sync()
    {
        if (_activeWhile())
        {
            if (_registered) return;
            IndestructibleGrantRegistry.AddGrant(_token, _predicate);
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
        IndestructibleGrantRegistry.RemoveGrant(_token);
        _registered = false;
    }
}
