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

    public MainPhase(IEventBus? eventBus = null) 
        : base(PhaseStateType.Main, eventBus)
    {
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
