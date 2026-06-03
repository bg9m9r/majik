using FluentAssertions;
using Majik.Core.Events;
using Majik.Core.StateMachine;
using Xunit;

namespace Majik.Core.Tests.StateMachine;

public class GameStateMachineTests
{
    [Fact]
    public void TransitionTo_PublishesTypedGameStateChangedEvent()
    {
        var bus = new EventBus();
        var sm = new GameStateMachine(bus); // ctor settles in Initializing before we subscribe

        GameStateChangedEvent? captured = null;
        bus.Subscribe<GameStateChangedEvent>(e => captured = e);

        sm.TransitionTo(GameStateType.Playing);

        captured.Should().NotBeNull();
        captured!.PreviousState.Should().Be(GameStateType.Initializing);
        captured.CurrentState.Should().Be(GameStateType.Playing);
    }

    [Fact]
    public void TransitionTo_DoesNotPublishPhaseChange_GameLifecycleNeverLeaksIntoPhaseChannel()
    {
        // Regression: GameStateMachine used to emit PhaseChangedEvent with
        // game-lifecycle names ("Mulligan", "Playing"), which the portal wrote
        // straight into its phase label. The lifecycle channel is now the
        // typed GameStateChangedEvent only; nothing on the phase/step channel
        // (StepStartedEvent) should fire from a pure game-state transition.
        var bus = new EventBus();
        var sm = new GameStateMachine(bus);

        var stepEvents = 0;
        bus.Subscribe<StepStartedEvent>(_ => stepEvents++);

        sm.TransitionTo(GameStateType.Mulligan);
        sm.TransitionTo(GameStateType.Playing);

        stepEvents.Should().Be(0);
    }
}
