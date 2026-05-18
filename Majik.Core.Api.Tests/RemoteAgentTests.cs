using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Xunit;

namespace Majik.Core.Api.Tests;

public class RemoteAgentTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public async Task Priority_AwaitsCommand_CompletesOnSubmit()
    {
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        var task = agent.ChoosePriorityActionAsync(ctx);
        task.IsCompleted.Should().BeFalse("nothing submitted yet");

        agent.Submit(new PassPriorityCommand { PlayerId = _alice.Id });
        var action = await task;

        action.Should().Be(PriorityAction.Pass);
    }

    [Fact]
    public async Task PlayLand_Submitted_ResolvesToActionWithCardLookup()
    {
        var land = new Land("Mountain") { Owner = _alice };
        var agent = new RemoteAgent(_alice, cardLookup: id => id == land.InstanceId ? land : null);
        var ctx = NewContext();

        var task = agent.ChoosePriorityActionAsync(ctx);
        agent.Submit(new PlayLandCommand(land.InstanceId) { PlayerId = _alice.Id });

        var action = await task;
        action.Should().BeOfType<PriorityAction.PlayLand>()
            .Which.Land.Should().BeSameAs(land);
    }

    [Fact]
    public async Task Submit_WrongPlayer_Throws()
    {
        var agent = new RemoteAgent(_alice);

        var act = () => agent.Submit(new PassPriorityCommand { PlayerId = Guid.NewGuid() });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*player*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Submit_WhenNothingPending_Throws()
    {
        var agent = new RemoteAgent(_alice);

        var act = () => agent.Submit(new PassPriorityCommand { PlayerId = _alice.Id });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no pending*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task MismatchedCommandType_Throws()
    {
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        _ = agent.ChoosePriorityActionAsync(ctx);

        var act = () => agent.Submit(new MulliganCommand(true) { PlayerId = _alice.Id });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*expected*PassPriorityCommand*");
        await Task.CompletedTask;
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice }, _alice, 1, PhaseStateType.Main, new Majik.Core.Stack.Stack());
}
