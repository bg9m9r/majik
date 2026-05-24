using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for Meddling Mage's printed static effect
/// (CR 601.3):
///   "Spells with the chosen name can't be cast."
///
/// While the source permanent is on the battlefield, the
/// <paramref name="chosenName"/> is registered into
/// <see cref="CastingRestrictions"/> via <c>AddNamedCardBlock</c>, and
/// <see cref="Majik.Core.Rules.ActionValidator"/> rejects any
/// <c>CastSpellAction</c> whose card name matches. When the Mage leaves
/// the battlefield the block is removed.
///
/// The chosen name is fixed at construction time and never changes for the
/// lifetime of this instance (a flickered Mage is a new game object with a
/// new ETB choice per CR 614.1c semantics — callers construct a new
/// lifecycle instance on each ETB).
///
/// Lifecycle mirrors <see cref="PithingNeedleStaticEffect"/>:
/// <list type="bullet">
///   <item>Subscribe to <see cref="CardMovedEvent"/> and register on
///         Attach.</item>
///   <item>Sync on every relevant zone move.</item>
///   <item>Detach unregisters from <see cref="CastingRestrictions"/>.</item>
/// </list>
/// </summary>
public sealed class MeddlingMageCastRestrictionEffect
{
    private readonly Permanent _source;
    private readonly IEventBus? _eventBus;
    private readonly string _chosenName;
    private readonly object _token = new();
    private readonly Action<GameEvent> _handler;
    private bool _attached;
    private bool _registered;

    /// <summary>
    /// Build a Meddling-Mage named-cast-block lifecycle.
    /// </summary>
    /// <param name="source">The Meddling Mage permanent gating the effect.
    /// Must be non-null.</param>
    /// <param name="chosenName">The card name chosen as Meddling Mage
    /// entered the battlefield (CR 614.1c). An empty string is treated as
    /// "no name chosen" and the effect no-ops (matching the fixture-only
    /// empty-name path).</param>
    /// <param name="eventBus">Event bus for <see cref="CardMovedEvent"/>.
    /// May be null — Attach will still sync once.</param>
    public MeddlingMageCastRestrictionEffect(
        Permanent source,
        string chosenName,
        IEventBus? eventBus)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _chosenName = chosenName ?? string.Empty;
        _eventBus = eventBus;
        _handler = OnEvent;
    }

    /// <summary>Whether the block is currently registered.</summary>
    public bool IsActive => _registered;

    /// <summary>The card name currently blocked (or empty if none).</summary>
    public string ChosenName => _chosenName;

    /// <summary>
    /// Subscribe to zone-move events and register if the source is already
    /// on the battlefield. Idempotent.
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
            if (string.IsNullOrEmpty(_chosenName)) return;
            CastingRestrictions.AddNamedCardBlock(_token, _chosenName);
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
        CastingRestrictions.RemoveNamedCardBlock(_token);
        _registered = false;
    }
}
