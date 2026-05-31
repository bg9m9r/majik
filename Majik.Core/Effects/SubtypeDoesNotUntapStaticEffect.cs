using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for a "permanents with subtype X don't
/// untap during their controllers' untap steps" symmetric static (CR
/// 502.1 — Choke for Islands; Smoke / Static Orb-adjacent later).
///
/// While the source permanent is on the battlefield, a subtype-scoped
/// entry is registered with <see cref="UntapStepRestrictions"/>. The
/// predicate fires against any permanent with the subtype regardless of
/// who controls it or whose untap step is current — same shape as the
/// printed oracle text for Choke.
///
/// Lifecycle mirrors <see cref="DoesNotUntapStaticEffect"/>.
/// </summary>
public sealed class SubtypeDoesNotUntapStaticEffect
{
    private readonly Permanent _source;
    private readonly CardSubtype _subtype;
    private readonly IEventBus? _eventBus;
    private readonly Action<CardMovedEvent> _handler;
    private readonly object _token = new();
    private bool _attached;
    private bool _registered;

    public SubtypeDoesNotUntapStaticEffect(
        Permanent source,
        CardSubtype subtype,
        IEventBus? eventBus)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _subtype = subtype;
        _eventBus = eventBus;
        _handler = OnEvent;
    }

    /// <summary>Whether the restriction is currently registered.</summary>
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

    private void OnEvent(CardMovedEvent e)
    {
        var moved = e;
        if (!ReferenceEquals(moved.Card, _source)) return;
        Sync();
    }

    private void Sync()
    {
        if (_source.Zone == ZoneType.Battlefield)
        {
            if (_registered) return;
            UntapStepRestrictions.MarkSubtypeDoesNotUntap(_token, _subtype);
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
