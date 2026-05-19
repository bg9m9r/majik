using System.Text.Json;
using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Xunit;

public class GameSnapshotTests
{
    [Fact]
    public async Task SaveSnapshot_ContainsLog_AfterSubmits()
    {
        var facade = GameFacade.Create("Alice", "Bob");
        await facade.StartAsync();
        var state = facade.GetState();
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = state.Players[0].Id });

        var snap = facade.SaveSnapshot();

        snap.State.Players.Should().HaveCount(2);
        snap.Log.Should().ContainSingle()
            .Which.Command.Should().BeOfType<PassPriorityCommand>();
    }

    [Fact]
    public async Task SaveSnapshotBytes_RoundTripsViaJson()
    {
        var facade = GameFacade.Create("Alice", "Bob");
        await facade.StartAsync();
        var state = facade.GetState();
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = state.Players[0].Id });
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = state.Players[1].Id });

        var bytes = facade.SaveSnapshotBytes();
        var restored = JsonSerializer.Deserialize<GameSnapshot>(bytes);

        restored.Should().NotBeNull();
        restored!.Log.Should().HaveCount(2);
        restored.Log.All(l => l.Command is PassPriorityCommand).Should().BeTrue();
        restored.State.GameId.Should().Be(facade.GameId);
    }
}
