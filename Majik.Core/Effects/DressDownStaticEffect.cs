using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Lifecycle binder for Dress Down (Modern Horizons 2, {1}{U}, Enchantment).
///
/// Oracle text:
///   "Flash
///    Creatures lose all abilities and have base power and toughness 1/1.
///    At the beginning of the end step, sacrifice Dress Down."
///
/// ## CR citations
/// - CR 613.6 — Layer 6 ability-removing effect ("lose all abilities").
/// - CR 613.7b — Layer 7b set-base P/T ("have base power and toughness 1/1").
/// - CR 702.8 — Flash (wired separately as a <see cref="Majik.Core.Abilities.KeywordAbility"/>).
/// - CR 603.1 / CR 500.4 — end-step trigger to sacrifice the source (wired
///   separately as a <see cref="Majik.Core.Abilities.TriggeredAbility"/>).
///
/// ## Implementation
/// The static half of Dress Down is exactly Humility's
/// <see cref="LoseAllAbilitiesEffect"/> plus a per-creature Layer 7b
/// <see cref="BecomesPTEffect"/>. While Dress Down is on the battlefield,
/// every creature in the battlefield's candidate pool loses its abilities
/// AND has its base P/T overwritten to 1/1. When Dress Down leaves the
/// battlefield (sacrifice on end step, removal in response, etc.) all
/// registrations are unregistered and creatures revert to printed P/T +
/// keywords.
///
/// ## Candidate-pool snapshot semantics
/// Per the implementation plan, the candidate pool is a snapshot of every
/// creature on the battlefield at Dress Down ETB time. Creatures that
/// enter the battlefield AFTER Dress Down are NOT scoped by this v1 wiring
/// — extending coverage to later-entering creatures would require a
/// CardMovedEvent watcher that grows the pool on creature ETB. This
/// matches the conservative scoping of Humility's existing
/// <see cref="LoseAllAbilitiesEffect"/>, which also accepts a static pool.
///
/// The lifecycle mirrors <see cref="RetypeLandsStaticEffect"/> and
/// <see cref="TorporOrbStaticEffect"/>: subscribe to
/// <see cref="CardMovedEvent"/>; register all effects on ETB, unregister
/// them on LTB. Idempotent via <c>_attached</c>.
/// </summary>
public sealed class DressDownStaticEffect
{
    private readonly Permanent _source;
    private readonly ContinuousEffectsService _effects;
    private readonly IEventBus? _eventBus;
    private readonly Func<IEnumerable<Creature>> _creaturePoolSource;
    private readonly Action<CardMovedEvent> _handler;
    private LoseAllAbilitiesEffect? _loseAbilities;
    private readonly List<BecomesPTEffect> _ptEffects = new();
    private bool _attached;

    /// <summary>
    /// Construct the lifecycle binder.
    /// </summary>
    /// <param name="source">Dress Down itself. The effect activates while
    /// <paramref name="source"/> is on the battlefield.</param>
    /// <param name="effects">The continuous-effects service to register
    /// the Layer 6 + Layer 7b effects against.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be null —
    /// in that case <see cref="Attach"/> will still register the effects if
    /// <paramref name="source"/> is already on the battlefield, but no zone
    /// transitions will be observed.</param>
    /// <param name="creaturePoolSource">Closure returning the set of
    /// creatures the effect should scope over. Invoked once at the moment
    /// of registration (ETB) — the snapshot is frozen for the lifetime of
    /// the active effect. Typically <c>() =&gt; allPlayers.SelectMany(p =&gt;
    /// p.Zones.Battlefield.GetCards()).OfType&lt;Creature&gt;()</c>.</param>
    public DressDownStaticEffect(
        Permanent source,
        ContinuousEffectsService effects,
        IEventBus? eventBus,
        Func<IEnumerable<Creature>> creaturePoolSource)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _eventBus = eventBus;
        _creaturePoolSource = creaturePoolSource ?? throw new ArgumentNullException(nameof(creaturePoolSource));
        _handler = OnEvent;
    }

    /// <summary>Whether the Layer 6 + Layer 7b effects are currently registered.</summary>
    public bool IsActive => _loseAbilities != null;

    /// <summary>
    /// Subscribe to zone-move events and register the effects if the source
    /// is already on the battlefield at attach time. Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus?.Subscribe(_handler);
        Sync();
    }

    /// <summary>
    /// Unsubscribe and unregister all effects. Idempotent.
    /// </summary>
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
        var shouldBeActive = _source.Zone == ZoneType.Battlefield;
        if (shouldBeActive && _loseAbilities == null)
        {
            // Snapshot the creature pool at ETB time (CR 613.6 candidate set).
            var pool = _creaturePoolSource().ToList();

            // Layer 6 — ability-removing effect (CR 613.6). One instance
            // covers every creature in the pool via the default
            // "applies to all" predicate.
            _loseAbilities = new LoseAllAbilitiesEffect(_source, pool);
            _effects.Register(_loseAbilities);

            // Layer 7b — per-creature set-base P/T to 1/1 (CR 613.7b).
            // BecomesPTEffect is target-keyed (ReferenceEquals), so one
            // instance per creature in the pool.
            foreach (var creature in pool)
            {
                var pt = new BecomesPTEffect(creature, 1, 1);
                _effects.Register(pt);
                _ptEffects.Add(pt);
            }
        }
        else if (!shouldBeActive)
        {
            Unregister();
        }
    }

    private void Unregister()
    {
        if (_loseAbilities != null)
        {
            _effects.Unregister(_loseAbilities);
            _loseAbilities = null;
        }
        foreach (var pt in _ptEffects)
        {
            _effects.Unregister(pt);
        }
        _ptEffects.Clear();
    }
}
