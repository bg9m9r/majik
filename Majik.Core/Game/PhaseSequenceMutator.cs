using Majik.Core.Events;
using Majik.Core.StateMachine;

namespace Majik.Core.Game;

/// <summary>
/// Owns the engine's pending extra-phase queue (CR 500.7–500.9 — extra
/// turns, extra phases, extra steps inserted by spells/abilities like
/// Time Walk or Aggravated Assault).
///
/// Pulled out of <see cref="PhaseManager"/> so the manager stays focused
/// on sequence iteration and step transitions; insertion logic lives
/// here and emits <see cref="ExtraPhaseAddedEvent"/> as it queues.
/// </summary>
public sealed class PhaseSequenceMutator
{
    private readonly IEventBus? _eventBus;
    private readonly Queue<PhaseStateType> _pending = new();

    public PhaseSequenceMutator(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    /// <summary>Count of phases waiting to be inserted before the natural
    /// next phase in the turn's sequence.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Queue a single phase for insertion (CR 500.8). Fires
    /// <see cref="ExtraPhaseAddedEvent"/>.</summary>
    public void AddExtraPhase(PhaseStateType phase)
    {
        _pending.Enqueue(phase);
        _eventBus?.Publish(new ExtraPhaseAddedEvent(phase));
    }

    /// <summary>Queue a complete extra combat phase (begin → declare
    /// attackers → declare blockers → combat damage → end of combat).
    /// Used by cards like Aggravated Assault.</summary>
    public void AddExtraCombatPhase()
    {
        AddExtraPhase(PhaseStateType.BeginningOfCombat);
        AddExtraPhase(PhaseStateType.DeclareAttackers);
        AddExtraPhase(PhaseStateType.DeclareBlockers);
        AddExtraPhase(PhaseStateType.CombatDamage);
        AddExtraPhase(PhaseStateType.EndOfCombat);
    }

    /// <summary>Queue an extra main phase (e.g. Seedborn Muse, Relentless
    /// Assault). Cards that grant an additional main phase grant a
    /// postcombat main (CR 505.1b — the extra main follows the combat it
    /// was created after), so the contextually-correct type is
    /// <see cref="PhaseStateType.PostCombatMain"/>.</summary>
    public void AddExtraMainPhase() => AddExtraPhase(PhaseStateType.PostCombatMain);

    /// <summary>Peek the next pending insertion without consuming it.
    /// Returns null when the queue is empty.</summary>
    public PhaseStateType? PeekNext() => _pending.Count > 0 ? _pending.Peek() : null;

    /// <summary>Attempt to consume the next pending insertion. Returns
    /// true and outputs the phase when one was available.</summary>
    public bool TryDequeue(out PhaseStateType phase)
    {
        if (_pending.Count == 0)
        {
            phase = default;
            return false;
        }
        phase = _pending.Dequeue();
        return true;
    }

    /// <summary>Drop every pending insertion (turn boundary reset).</summary>
    public void Clear() => _pending.Clear();
}
