using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Xunit;

public class FacadeActionLogTests
{
    [Fact]
    public async Task SubmitAsync_AppendsCommandToLog()
    {
        var facade = GameFacade.Create("Alice", "Bob");
        await facade.StartAsync();
        var alice = facade.GetState().Players[0].Id;

        facade.Log.Count.Should().Be(0);

        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = alice });

        facade.Log.Count.Should().Be(1);
        facade.Log.Actions[0].Command.Should().BeOfType<PassPriorityCommand>();
    }

    [Fact]
    public async Task LogPreservesSubmissionOrder()
    {
        var facade = GameFacade.Create("Alice", "Bob");
        await facade.StartAsync();
        var state = facade.GetState();
        var alice = state.Players[0].Id;
        var bob = state.Players[1].Id;

        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = alice });
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = bob });

        facade.Log.Actions.Select(a => a.Command.PlayerId)
            .Should().Equal(alice, bob);
    }
}
