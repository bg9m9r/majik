using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Game.Phases;

/// <summary>
/// Untap step implementation.
/// Untaps all permanents controlled by the active player.
/// </summary>
public class UntapStep : PhaseState
{
    private readonly IEventBus? _eventBus;

    public UntapStep(IEventBus? eventBus = null) 
        : base(PhaseStateType.Untap, eventBus)
    {
        _eventBus = eventBus;
    }

    public override void OnEnter()
    {
        base.OnEnter();
        // Untap logic will be implemented when we have permanents
        // For now, just fire the event
    }

    public override void OnExit()
    {
        base.OnExit();
    }
}
