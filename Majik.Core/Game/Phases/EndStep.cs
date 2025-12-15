using Majik.Core.Events;
using Majik.Core.StateMachine;

namespace Majik.Core.Game.Phases;

/// <summary>
/// End step implementation.
/// Triggers end step triggers and effects.
/// </summary>
public class EndStep : PhaseState
{
    private readonly IEventBus? _eventBus;

    public EndStep(IEventBus? eventBus = null) 
        : base(PhaseStateType.End, eventBus)
    {
        _eventBus = eventBus;
    }

    public override void OnEnter()
    {
        base.OnEnter();
        // End step logic will be implemented when we have triggers
        // For now, just fire the event
    }

    public override void OnExit()
    {
        base.OnExit();
    }
}
