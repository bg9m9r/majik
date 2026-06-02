using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for a "you may play [filter] from the top of your
/// library" continuous static (CR 601.3e / CR 305.6), optionally "playing with
/// the top card revealed" (CR 715.4). Canonical callers are Courser of Kruphix
/// (lands), Augur of Autumn (lands; + creatures once the Coven cast path lands),
/// and Oracle of Mul Daya (lands).
///
/// While the source permanent is on the battlefield, a grant is registered into
/// <see cref="LibraryTopPlayPermissions"/> for the controller; when the source
/// leaves the battlefield the grant is removed (CR 603.6e — a static functions
/// only while its source is on the battlefield). Lifecycle mirrors
/// <see cref="FlashGrantStaticEffect"/>:
/// <list type="bullet">
///   <item>Subscribe to <see cref="CardMovedEvent"/> on Attach.</item>
///   <item>Sync on Attach (active if the source is already on the
///         battlefield).</item>
///   <item>On every move event for the source, re-sync.</item>
///   <item>Detach unsubscribes and removes the registration.</item>
/// </list>
/// </summary>
public sealed class LibraryTopPlayStaticEffect
{
    private readonly Permanent _source;
    private readonly Player _controller;
    private readonly TopPlayFilter _filter;
    private readonly bool _revealsTop;
    private readonly IEventBus? _eventBus;
    private readonly Action<CardMovedEvent> _handler;
    private readonly object _token = new();
    private bool _attached;
    private bool _registered;

    /// <summary>
    /// Build a play-from-top lifecycle.
    /// </summary>
    /// <param name="source">The permanent gating the grant — the grant is live
    /// only while this permanent is on the battlefield.</param>
    /// <param name="controller">The player the grant benefits.</param>
    /// <param name="filter">Which top-of-library cards become legal play
    /// sources (lands / creatures / any).</param>
    /// <param name="eventBus">Event bus for <see cref="CardMovedEvent"/>. May be
    /// null — Attach still syncs once against the source's current zone.</param>
    /// <param name="revealsTop">Whether the source also plays with the top card
    /// revealed (CR 715.4). Default true.</param>
    public LibraryTopPlayStaticEffect(
        Permanent source,
        Player controller,
        TopPlayFilter filter,
        IEventBus? eventBus,
        bool revealsTop = true)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _filter = filter;
        _revealsTop = revealsTop;
        _eventBus = eventBus;
        _handler = OnEvent;
    }

    /// <summary>Whether the grant is currently registered.</summary>
    public bool IsActive => _registered;

    /// <summary>
    /// Subscribe to zone-move events and register if the source is already on
    /// the battlefield. Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus?.Subscribe(_handler);
        Sync();
    }

    /// <summary>Unsubscribe and remove the grant. Idempotent.</summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.Unsubscribe(_handler);
        Unregister();
    }

    private void OnEvent(CardMovedEvent e)
    {
        // Only the source moving in/out of the battlefield changes the grant.
        if (!ReferenceEquals(e.Card, _source)) return;
        Sync();
    }

    private void Sync()
    {
        if (_source.Zone == ZoneType.Battlefield)
        {
            if (_registered) return;
            LibraryTopPlayPermissions.AddGrant(_token, _controller, _filter, _revealsTop);
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
        LibraryTopPlayPermissions.RemoveGrant(_token);
        _registered = false;
    }
}
