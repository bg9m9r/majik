using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for a "target players can't cast spells
/// from anywhere other than their hands" static effect (CR 113.6 —
/// Drannith Magistrate and friends; CR 114 — emblem-with-static
/// surface).
///
/// While the source permanent is on the battlefield (or, for an
/// emblem, for the rest of the game), a cast-from-hand-only restriction
/// is registered against each player produced by the supplied
/// <c>affectedPlayersResolver</c>. When the source leaves the
/// battlefield, all entries keyed by this lifecycle instance are
/// removed.
///
/// Lifecycle mirrors <see cref="SorcerySpeedRestrictionEffect"/>:
/// <list type="bullet">
///   <item>Subscribe to <see cref="CardMovedEvent"/> on Attach.</item>
///   <item>Sync on Attach (active if the source is already on the
///         battlefield).</item>
///   <item>On every relevant move event, re-sync.</item>
///   <item>Detach unsubscribes and removes the registration.</item>
/// </list>
///
/// Emblems are not permanents and don't ETB/LTB; for emblem-with-static
/// support, pass <c>permanentSource: null</c> + <c>activeWhile: () =>
/// true</c>. The effect then registers immediately on Attach and never
/// detaches by zone event (emblems last for the rest of the game per
/// CR 114).
/// </summary>
public sealed class CastFromHandOnlyRestrictionEffect
{
    private readonly Permanent? _source;
    private readonly IEventBus? _eventBus;
    private readonly Func<IReadOnlyList<Player>> _affectedPlayersResolver;
    private readonly Func<bool> _activeWhile;
    private readonly Action<GameEvent> _handler;
    private readonly object _token = new();
    private bool _attached;
    private bool _registered;

    /// <summary>
    /// Build a cast-from-hand-only-restriction lifecycle.
    /// </summary>
    /// <param name="source">The permanent gating the effect. May be null
    /// for emblem-style "always on" effects — pair with <paramref
    /// name="activeWhile"/> = <c>() =&gt; true</c>.</param>
    /// <param name="eventBus">Event bus for <see cref="CardMovedEvent"/>.
    /// May be null — Attach will still sync once.</param>
    /// <param name="affectedPlayersResolver">Returns the set of players
    /// to restrict each time the effect activates. Resolved at sync time
    /// so opponents added/removed mid-game (multiplayer) are picked up
    /// next time the source flickers.</param>
    /// <param name="activeWhile">Override the default "source is on the
    /// battlefield" gate. Defaults to <c>source.Zone == Battlefield</c>
    /// when <paramref name="source"/> is non-null, or <c>true</c>
    /// otherwise.</param>
    public CastFromHandOnlyRestrictionEffect(
        Permanent? source,
        IEventBus? eventBus,
        Func<IReadOnlyList<Player>> affectedPlayersResolver,
        Func<bool>? activeWhile = null)
    {
        _source = source;
        _eventBus = eventBus;
        _affectedPlayersResolver = affectedPlayersResolver
            ?? throw new ArgumentNullException(nameof(affectedPlayersResolver));
        _activeWhile = activeWhile ?? DefaultActiveGate;
        _handler = OnEvent;
    }

    /// <summary>Whether the restriction is currently registered.</summary>
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
    /// Unsubscribe and remove the registration. Idempotent.
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
            var affected = _affectedPlayersResolver();
            if (affected == null) return;
            foreach (var p in affected)
            {
                if (p == null) continue;
                CastingRestrictions.AddCastFromHandOnlyRestriction(_token, p);
            }
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
        CastingRestrictions.RemoveCastFromHandOnlyRestriction(_token);
        _registered = false;
    }
}
