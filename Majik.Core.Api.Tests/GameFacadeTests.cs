using System.Text.Json;
using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Xunit;

namespace Majik.Core.Api.Tests;

public class GameFacadeTests
{
    [Fact]
    public async Task NewGame_GetState_Returns2PlayerSnapshot()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        await facade.StartAsync();

        var state = facade.GetState();

        state.Players.Should().HaveCount(2);
        state.Players.Select(p => p.Name).Should().Equal("Alice", "Bob");
        state.GameId.Should().NotBe(Guid.Empty);
        state.ActivePlayerId.Should().Be(state.Players[0].Id);
    }

    [Fact]
    public async Task PassPriorityCommand_FromBothPlayers_DrainsPriorityRound()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        await facade.StartAsync();
        var state = facade.GetState();
        var alice = state.Players[0].Id;
        var bob = state.Players[1].Id;

        // Alice has priority first
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = alice });
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = bob });

        // Round resolved (stack empty + all passed).
        facade.IsRoundComplete.Should().BeTrue();
    }

    [Fact]
    public async Task Subscribe_DeliversEventDtoForCardMoves()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var captured = new List<EventDto>();
        facade.Subscribe(captured.Add);

        await facade.StartAsync();

        captured.Should().NotBeEmpty();
        captured.Any(e => e.Type == nameof(Majik.Core.Domain.DomainEvents.PriorityReceivedEvent))
            .Should().BeTrue();
    }

    [Fact]
    public async Task SubmitCommand_FromWrongPlayer_Throws()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        await facade.StartAsync();
        var state = facade.GetState();
        var bob = state.Players[1].Id;

        // Bob doesn't have priority — Alice does.
        var act = async () => await facade.SubmitAsync(new PassPriorityCommand { PlayerId = bob });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
