using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for a "this player has shroud" static effect
/// (CR 702.18 — Solitary Confinement's "You have shroud" is the canonical
/// caller). Sibling of <see cref="PlayerHexproofEffect"/>; differs only in
/// the registry channel it feeds — shroud blocks ALL targeting, including the
/// player's own spells and abilities (CR 702.18a), whereas hexproof blocks
/// only opponents' (CR 702.11).
///
/// While the source permanent is on the battlefield, a player-shroud grant is
/// registered against each player produced by the supplied
/// <c>affectedPlayersResolver</c> into
/// <see cref="PlayerStaticAbilities"/>. When the source leaves the
/// battlefield the grant is removed. Lifecycle mirrors
/// <see cref="PlayerHexproofEffect"/> (subscribe to
/// <see cref="CardMovedEvent"/>, sync on Attach, re-sync on each relevant
/// move, unregister on Detach).
/// </summary>
public sealed class PlayerShroudEffect
{
    private readonly Permanent? _source;
    private readonly IEventBus? _eventBus;
    private readonly Func<IReadOnlyList<Player>> _affectedPlayersResolver;
    private readonly Func<bool> _activeWhile;
    private readonly Action<CardMovedEvent> _handler;
    private readonly object _token = new();
    private bool _attached;
    private bool _registered;

    public PlayerShroudEffect(
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

    /// <summary>Whether the shroud grant is currently registered.</summary>
    public bool IsActive => _registered;

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus?.Subscribe(_handler);
        Sync();
    }

    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.Unsubscribe(_handler);
        Unregister();
    }

    private bool DefaultActiveGate()
    {
        if (_source == null) return true;
        return _source.Zone == ZoneType.Battlefield;
    }

    private void OnEvent(CardMovedEvent e)
    {
        if (_source != null && !ReferenceEquals(e.Card, _source)) return;
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
                PlayerStaticAbilities.AddShroud(_token, p);
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
        PlayerStaticAbilities.RemoveShroud(_token);
        _registered = false;
    }
}
