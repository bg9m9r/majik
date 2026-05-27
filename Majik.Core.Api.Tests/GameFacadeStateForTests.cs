using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Cards;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Tests for Slice 2a: per-viewer GameStateDto.YouPlayerId stamping.
/// CR 706 — each viewer receives a snapshot scoped to their seat; the
/// YouPlayerId field surfaces the viewer's own engine seat id so the portal
/// can self-identify without a second round-trip.
/// </summary>
public class GameFacadeStateForTests
{
    [Fact]
    public void GetStateFor_BobViewer_YouPlayerIdIsBobId()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());

        var state = facade.GetStateFor(facade.Bob.Id);

        state.Should().NotBeNull();
        state!.YouPlayerId.Should().Be(facade.Bob.Id,
            "YouPlayerId must reflect the requesting viewer's own seat id");
    }

    [Fact]
    public void GetStateFor_AliceViewer_YouPlayerIdIsAliceId()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());

        var state = facade.GetStateFor(facade.Alice.Id);

        state.Should().NotBeNull();
        state!.YouPlayerId.Should().Be(facade.Alice.Id,
            "YouPlayerId must reflect the requesting viewer's own seat id");
    }

    [Fact]
    public void GetState_SpectatorView_YouPlayerIdIsNull()
    {
        // GetState() (no viewer arg) is the spectator / all-revealed view.
        // YouPlayerId must be null — there is no "you" in a spectator view.
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());

        var state = facade.GetState();

        state.YouPlayerId.Should().BeNull(
            "spectator view has no owning seat; YouPlayerId must be null");
    }
}
