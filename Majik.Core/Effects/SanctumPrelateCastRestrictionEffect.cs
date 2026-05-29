using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for Sanctum Prelate's printed static effect
/// (CR 601.3):
///   "Noncreature spells with mana value equal to the chosen number can't
///    be cast."
///
/// While the source permanent is on the battlefield, the
/// <paramref name="chosenNumber"/> is registered into
/// <see cref="CastingRestrictions"/> via <c>AddNoncreatureManaValueBlock</c>,
/// and <see cref="Majik.Core.Rules.ActionValidator"/> rejects any
/// <c>CastSpellAction</c> whose card is a noncreature spell whose mana value
/// matches. When the Prelate leaves the battlefield the block is removed.
///
/// The chosen number is fixed at construction time and never changes for the
/// lifetime of this instance — the choice is made "as this creature enters"
/// (CR 614.1c). A flickered Prelate is a new game object with a new ETB
/// choice, so callers construct a new lifecycle instance on each ETB.
///
/// Lifecycle mirrors <see cref="MeddlingMageCastRestrictionEffect"/>:
/// <list type="bullet">
///   <item>Subscribe to <see cref="CardMovedEvent"/> and register on
///         Attach.</item>
///   <item>Sync on every relevant zone move.</item>
///   <item>Detach unregisters from <see cref="CastingRestrictions"/>.</item>
/// </list>
/// </summary>
public sealed class SanctumPrelateCastRestrictionEffect
{
    private readonly Permanent _source;
    private readonly IEventBus? _eventBus;
    private readonly int _chosenNumber;
    private readonly object _token = new();
    private readonly Action<GameEvent> _handler;
    private bool _attached;
    private bool _registered;

    /// <summary>
    /// Build a Sanctum-Prelate noncreature-mana-value-block lifecycle.
    /// </summary>
    /// <param name="source">The Sanctum Prelate permanent gating the effect.
    /// Must be non-null.</param>
    /// <param name="chosenNumber">The number chosen as Sanctum Prelate
    /// entered the battlefield (CR 614.1c). Noncreature spells with mana
    /// value equal to this number can't be cast. A negative number is
    /// treated as "no number chosen" and the effect no-ops (the
    /// fixture-only / shape path); zero is a valid choice (blocks mv-0
    /// noncreature spells like Ornithopter is a creature — but Memnite-style
    /// mv-0 noncreature spells, Mox Opal, etc. are blocked).</param>
    /// <param name="eventBus">Event bus for <see cref="CardMovedEvent"/>.
    /// May be null — Attach will still sync once.</param>
    public SanctumPrelateCastRestrictionEffect(
        Permanent source,
        int chosenNumber,
        IEventBus? eventBus)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _chosenNumber = chosenNumber;
        _eventBus = eventBus;
        _handler = OnEvent;
    }

    /// <summary>Whether the block is currently registered.</summary>
    public bool IsActive => _registered;

    /// <summary>The mana value currently blocked.</summary>
    public int ChosenNumber => _chosenNumber;

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
            // A negative number means "no choice made" (shape-only path) —
            // no restriction. Zero is a legitimate choice.
            if (_chosenNumber < 0) return;
            CastingRestrictions.AddNoncreatureManaValueBlock(_token, _chosenNumber);
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
        CastingRestrictions.RemoveNoncreatureManaValueBlock(_token);
        _registered = false;
    }
}
