using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Static continuous effect for Torpor Orb (Scars of Mirrodin, {2}, Artifact).
///
/// Oracle text: "Creatures entering the battlefield don't cause abilities to trigger."
///
/// CR 614 (replacement/continuous effects) + CR 603 (triggered abilities):
/// While this effect is active — i.e. the source Torpor Orb is on the
/// battlefield — the <see cref="TriggerManager.CreatureEtbTriggerSuppressionCount"/>
/// is incremented. That counter gates the trigger-evaluation loop in
/// <see cref="TriggerManager.EvaluateTriggers"/> so that no triggered ability
/// whose trigger event is a creature entering the battlefield is queued.
///
/// Multiple simultaneous Torpor Orbs are handled correctly by incrementing
/// once per Orb; the count drops to zero only when all Orbs leave.
///
/// Lifecycle:
///   - Call <see cref="Attach"/> once after creating the effect to subscribe
///     to <see cref="CardMovedEvent"/> and sync initial state.
///   - Call <see cref="Detach"/> when the source Orb is permanently removed
///     from the game (e.g. game teardown) to unsubscribe.
///
/// The design intentionally avoids a full <see cref="IStaticAbility"/> /
/// <see cref="StaticAbilityManager"/> registration because the suppression
/// must toggle exactly when the Orb crosses zone boundaries — an event-driven
/// model is cleaner than polling <c>ApplyStaticAbilities()</c>.
/// </summary>
public sealed class TorporOrbStaticEffect
{
    private readonly ICard _orb;
    private readonly TriggerManager _triggerManager;
    private readonly IEventBus? _eventBus;
    private readonly Action<CardMovedEvent> _handler;
    private bool _attached;
    private bool _currentlyActive; // tracks whether we've incremented the counter

    public TorporOrbStaticEffect(ICard orb, TriggerManager triggerManager, IEventBus? eventBus = null)
    {
        _orb = orb ?? throw new ArgumentNullException(nameof(orb));
        _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
        _eventBus = eventBus;
        _handler = OnEvent;
    }

    /// <summary>
    /// Subscribe to zone-move events and apply the suppression if the Orb is
    /// already on the battlefield at attach time.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus?.Subscribe(_handler);
        SyncSuppression();
    }

    /// <summary>
    /// Unsubscribe from events and withdraw suppression if currently active.
    /// </summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.Unsubscribe(_handler);
        if (_currentlyActive)
        {
            _triggerManager.CreatureEtbTriggerSuppressionCount--;
            _currentlyActive = false;
        }
    }

    /// <summary>Whether the suppression is currently applied.</summary>
    public bool IsActive => _currentlyActive;

    private void OnEvent(CardMovedEvent e)
    {
        var moved = e;
        if (!ReferenceEquals(moved.Card, _orb)) return;
        SyncSuppression();
    }

    private void SyncSuppression()
    {
        var shouldBeActive = _orb.Zone == ZoneType.Battlefield;

        if (shouldBeActive && !_currentlyActive)
        {
            _triggerManager.CreatureEtbTriggerSuppressionCount++;
            _currentlyActive = true;
        }
        else if (!shouldBeActive && _currentlyActive)
        {
            _triggerManager.CreatureEtbTriggerSuppressionCount--;
            _currentlyActive = false;
        }
    }
}
