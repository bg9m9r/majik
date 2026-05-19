using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>Verifies that the engine's transition into an awaiting-input
/// state surfaces through GameFacade.SubscribePrompts. The transport
/// layer relies on this to know when to push a prompt envelope.</summary>
public class PromptSubscribeTests
{
    [Fact]
    public async Task SubscribePrompts_FiresOnceWhenEngineAwaitsCommand()
    {
        var facade = GameFacade.Create("Alice", "Bob");
        var prompts = new List<PromptDto>();
        using var subscription = facade.SubscribePrompts(prompts.Add);

        await facade.StartAsync();

        prompts.Should().NotBeEmpty("the engine prompts the active player on start");
        prompts[0].GameId.Should().Be(facade.GameId);
        prompts[0].ExpectedKinds.Should().NotBeEmpty();
        // Alice has priority first.
        prompts[0].PlayerId.Should().Be(facade.GetState().Players[0].Id);
    }

    [Fact]
    public async Task SubscribePrompts_DisposeStopsDelivery()
    {
        var facade = GameFacade.Create("Alice", "Bob");
        var prompts = new List<PromptDto>();
        var subscription = facade.SubscribePrompts(prompts.Add);

        subscription.Dispose();
        await facade.StartAsync();

        prompts.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribePrompts_NextPromptFiresAfterCommandSubmitted()
    {
        var facade = GameFacade.Create("Alice", "Bob");
        var prompts = new List<PromptDto>();
        var secondPrompt = new TaskCompletionSource();
        using var subscription = facade.SubscribePrompts(p =>
        {
            prompts.Add(p);
            if (prompts.Count == 2) secondPrompt.TrySetResult();
        });

        await facade.StartAsync();
        var alice = facade.GetState().Players[0].Id;
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = alice });

        await secondPrompt.Task.WaitAsync(TimeSpan.FromSeconds(2));

        prompts.Should().HaveCountGreaterThanOrEqualTo(2,
            "passing priority hands the prompt to Bob, which should fire a new envelope");
    }
}
