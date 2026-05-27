using Majik.Core.Events;
using Majik.Core.StateMachine;

namespace Majik.Core.Game.Phases;

/// <summary>
/// Main phase implementation.
/// Players can cast spells, activate abilities, and play lands.
/// </summary>
public class MainPhase : PhaseState
{
    private readonly IEventBus? _eventBus;

    /// <summary>
    /// Construct a main phase. CR 505 splits the main phase into a
    /// precombat (<see cref="PhaseStateType.PreCombatMain"/>) and postcombat
    /// (<see cref="PhaseStateType.PostCombatMain"/>) instance; the type is
    /// carried in from the sequence so each registers under its own label.
    /// Defaults to the precombat main when unspecified.
    /// </summary>
    public MainPhase(PhaseStateType type = PhaseStateType.PreCombatMain, IEventBus? eventBus = null)
        : base(type, eventBus)
    {
        if (!type.IsMain())
        {
            throw new ArgumentOutOfRangeException(
                nameof(type), type, "MainPhase must be a main phase type (PreCombatMain or PostCombatMain).");
        }
        _eventBus = eventBus;
    }

    public override void OnEnter()
    {
        base.OnEnter();
        // Main phase logic will be implemented when we have stack/priority
        // For now, just fire the event
    }

    public override void OnExit()
    {
        base.OnExit();
    }
}
