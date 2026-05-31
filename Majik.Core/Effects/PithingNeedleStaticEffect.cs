using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for Pithing Needle's printed static effect
/// (CR 602.5c — "Activated abilities of sources with the chosen name
/// can't be activated unless they're mana abilities.").
///
/// Lifecycle mirrors <see cref="SorcerySpeedRestrictionEffect"/>:
/// <list type="bullet">
///   <item>The chosen card name is resolved on Attach (or on the first
///         ETB sync if the source isn't yet on the battlefield) via the
///         supplied <c>nameSelector</c> — typically a <see cref="Player"/>
///         agent prompt closure.</item>
///   <item>While the source permanent is on the battlefield, a
///         name-restriction is registered into
///         <see cref="ActivatedAbilityRestrictions"/>.</item>
///   <item>On Detach (or on LTB via <see cref="CardMovedEvent"/>), the
///         registration is removed.</item>
/// </list>
///
/// The chosen name persists across flickers via the closure-captured
/// selector — re-entering the battlefield reprompts (CR 614.1c semantics:
/// a flickered Needle is a new object, so it chooses again).
///
/// The selector is invoked at most once per registration; the resolved
/// name is cached for the lifetime of the active registration so the
/// agent isn't polled on every consult. When the source leaves the
/// battlefield, the cached name is cleared so a flicker reprompts.
/// </summary>
public sealed class PithingNeedleStaticEffect
{
    private readonly Permanent? _source;
    private readonly IEventBus? _eventBus;
    private readonly Func<Player, string> _nameSelector;
    private readonly Player _controller;
    private readonly object _token = new();
    private readonly Action<CardMovedEvent> _handler;
    private bool _attached;
    private bool _registered;
    private string? _chosenName;

    /// <summary>
    /// Build a Pithing-Needle lifecycle.
    /// </summary>
    /// <param name="source">The permanent gating the effect. May be null
    /// for test scaffolding — pair with <paramref name="eventBus"/>
    /// = null and Attach() will sync once.</param>
    /// <param name="controller">The player choosing the name (CR 605.1c
    /// — the activating player of the ETB trigger). Passed into the
    /// selector closure.</param>
    /// <param name="nameSelector">Resolves the chosen card name at the
    /// moment the Needle enters the battlefield. Called at most once
    /// per registration; the resolved name is cached until the source
    /// leaves the battlefield.</param>
    /// <param name="eventBus">Event bus for <see cref="CardMovedEvent"/>.
    /// May be null — Attach will still sync once.</param>
    public PithingNeedleStaticEffect(
        Permanent? source,
        Player controller,
        Func<Player, string> nameSelector,
        IEventBus? eventBus)
    {
        _source = source;
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _nameSelector = nameSelector ?? throw new ArgumentNullException(nameof(nameSelector));
        _eventBus = eventBus;
        _handler = OnEvent;
    }

    /// <summary>Whether the suppression is currently registered.</summary>
    public bool IsActive => _registered;

    /// <summary>The chosen card name, or null if not yet registered.</summary>
    public string? ChosenName => _chosenName;

    /// <summary>
    /// Subscribe to zone-move events and register if the source is already
    /// on the battlefield. Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus?.Subscribe(_handler);
        Sync();
    }

    /// <summary>
    /// Unsubscribe and remove the registration. Idempotent.
    /// </summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.Unsubscribe(_handler);
        Unregister();
    }

    private bool ActiveGate()
    {
        if (_source == null) return true;
        return _source.Zone == ZoneType.Battlefield;
    }

    private void OnEvent(CardMovedEvent e)
    {
        var moved = e;
        if (_source != null && !ReferenceEquals(moved.Card, _source)) return;
        Sync();
    }

    private void Sync()
    {
        if (ActiveGate())
        {
            if (_registered) return;
            // Resolve the chosen name lazily — the agent might not be
            // ready to prompt at construction time, but is by the time
            // the Needle resolves and ETBs.
            var name = _nameSelector(_controller);
            if (string.IsNullOrEmpty(name))
            {
                // Selector declined to choose — nothing to register.
                // Match Magic semantics: the choice is required, so an
                // empty result here is a fixture-only path; we just no-op
                // rather than throwing.
                return;
            }
            _chosenName = name;
            ActivatedAbilityRestrictions.AddNameRestriction(_token, name);
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
        ActivatedAbilityRestrictions.RemoveNameRestriction(_token);
        _registered = false;
        _chosenName = null;
    }
}
