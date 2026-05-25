using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Events;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Regression guard for the GameFacade priority-loop phase wiring. The
/// loop's <c>phaseAccessor</c> used to be hardcoded to
/// <see cref="PhaseStateType.Main"/>, so RemoteAgent's
/// <c>ExpectedCommandKinds</c> always advertised PlayLand and the engine
/// happily applied a <see cref="PlayLandCommand"/> in any step. That
/// violates CR 305.2 / CR 116.2a (lands are sorcery-speed, own-turn only).
///
/// The fix wires <c>phaseAccessor</c> to the facade's <c>_currentPhase</c>
/// field (kept fresh by PhaseStartedEvent / StepStartedEvent subscriptions).
/// This test pins that behaviour down: with the facade's current phase
/// flipped to Upkeep, a PlayLandCommand must be rejected before it can
/// move the land out of hand.
/// </summary>
public class PlayLandPhaseGateTests
{
    [Fact]
    public async Task PlayLandCommand_OutsideMainPhase_IsRejected_LandStaysInHand()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());

        // Seed Alice's hand with a Mountain. Owner + Hand zone are required so
        // RemoteAgent.BuildPriorityKinds detects "land in hand" when deciding
        // whether PlayLandCommand is offered.
        var land = new Land("Mountain");
        land.SetOwner(facade.Alice);
        land.SetZone(ZoneType.Hand);
        facade.Alice.Zones.Hand.AddCard(land);

        // Flip the facade's tracked phase to Upkeep BEFORE the priority loop
        // starts. The facade subscribes to StepStartedEvent and updates
        // _currentPhase, which (post-fix) is what the priority loop's
        // phaseAccessor returns. With the bug present, the loop ignored this
        // and always passed Main, so PlayLand was always offered.
        facade.EventBus_Publish(new StepStartedEvent(PhaseStateType.Upkeep, facade.Alice));

        await facade.StartAsync();

        var aliceId = facade.GetState().Players[0].Id;

        // CR 305.2 — lands are sorcery-speed and only on your own turn.
        // RemoteAgent's prompt-kind gate (BuildPriorityKinds) must omit
        // PlayLandCommand outside a main phase; submitting it then trips the
        // "Engine expected ... got PlayLandCommand" rejection. Pre-fix, this
        // succeeded and dropped the Mountain onto the battlefield mid-upkeep.
        var act = async () => await facade.SubmitAsync(
            new PlayLandCommand(land.InstanceId) { PlayerId = aliceId });

        await act.Should().ThrowAsync<InvalidOperationException>();

        facade.Alice.Zones.Hand.GetCards().Should().Contain(land,
            "the land must stay in hand when the play-land command is rejected.");
        facade.Alice.Zones.Battlefield.GetCards().Should().NotContain(land,
            "no land should have entered the battlefield outside a main phase.");
    }
}
