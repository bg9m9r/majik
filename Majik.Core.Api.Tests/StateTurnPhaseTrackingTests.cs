using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>Verifies GameFacade.GetState reflects the engine's actual
/// turn number, current phase, and active player rather than the
/// hard-coded values that lived in the facade before slice 14.</summary>
public class StateTurnPhaseTrackingTests
{
    [Fact]
    public void GetState_DefaultsToTurn1MainOnFreshFacade()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());

        var state = facade.GetState();

        state.TurnNumber.Should().Be(1);
        state.Phase.Should().Be(PhaseStateType.PreCombatMain.ToString());
    }

    [Fact]
    public void GetState_TurnNumber_PicksUpTurnStartedEvent()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var bob = facade.GetState().Players[1];

        facade.EventBus_Publish(new TurnStartedEvent(new Player(bob.Name), turnNumber: 7));

        facade.GetState().TurnNumber.Should().Be(7);
    }

    [Fact]
    public void GetState_Phase_PicksUpPhaseStartedEvent()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var alice = new Player("Alice");

        facade.EventBus_Publish(new PhaseStartedEvent(PhaseStateType.DeclareAttackers, alice));

        facade.GetState().Phase.Should().Be(PhaseStateType.DeclareAttackers.ToString());
    }

    // CR 505 — since Slice 3 the phase value itself carries the pre/post
    // distinction (PreCombatMain / PostCombatMain), so the snapshot label
    // comes straight from the phase and no longer depends on the tracked
    // TurnStateType to disambiguate.
    [Fact]
    public void GetState_Phase_PreCombatMain_LabelsPreCombatMain()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var alice = new Player("Alice");

        facade.EventBus_Publish(new TurnStateChangedEvent(TurnStateType.TurnBeginning, TurnStateType.PreCombatMain));
        facade.EventBus_Publish(new StepStartedEvent(PhaseStateType.PreCombatMain, alice));

        facade.GetState().Phase.Should().Be(PhaseLabelResolver.PreCombatMain);
    }

    [Fact]
    public void GetState_Phase_PostCombatMain_LabelsPostCombatMain_IndependentOfTurnState()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var alice = new Player("Alice");

        // Even with the turn-state left at PreCombatMain, the postcombat phase
        // value alone produces the PostCombatMain label.
        facade.EventBus_Publish(new TurnStateChangedEvent(TurnStateType.TurnBeginning, TurnStateType.PreCombatMain));
        facade.EventBus_Publish(new StepStartedEvent(PhaseStateType.PostCombatMain, alice));

        facade.GetState().Phase.Should().Be(PhaseLabelResolver.PostCombatMain);
    }

    [Fact]
    public void GetState_Phase_NonMain_IgnoresTurnState()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var alice = new Player("Alice");

        facade.EventBus_Publish(new TurnStateChangedEvent(TurnStateType.TurnBeginning, TurnStateType.PreCombatMain));
        facade.EventBus_Publish(new StepStartedEvent(PhaseStateType.Upkeep, alice));

        facade.GetState().Phase.Should().Be(PhaseStateType.Upkeep.ToString());
    }
}

/// <summary>Test seam — the facade's bus is private. This file-scoped
/// extension exposes Publish for tests that need to simulate engine
/// events without driving the full priority loop.</summary>
internal static class FacadeTestExtensions
{
    public static void EventBus_Publish<T>(this GameFacade facade, T e) where T : GameEvent
    {
        // Use reflection to reach the private _bus field.
        var field = typeof(GameFacade).GetField("_bus",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var bus = (IEventBus)field.GetValue(facade)!;
        bus.Publish(e);
    }
}
