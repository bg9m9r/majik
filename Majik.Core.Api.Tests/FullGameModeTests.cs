using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Cards;
using Majik.Core.Api.Dtos;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>Covers the Phase 10 entry point — GameFacade.StartFullGameAsync
/// drives the full GameDriver pipeline (shuffle, mulligan, multi-turn).
/// The legacy single-priority-round StartAsync is still covered by the
/// rest of the suite.</summary>
public class FullGameModeTests
{
    [Fact]
    public async Task StartFullGameAsync_PromptsForMulliganFirst()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var prompts = new List<PromptDto>();
        var firstPrompt = new TaskCompletionSource();
        using var subscription = facade.SubscribePrompts(p =>
        {
            prompts.Add(p);
            firstPrompt.TrySetResult();
        });

        await facade.StartFullGameAsync(maxTurns: 1);

        await firstPrompt.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // GameDriver runs the mulligan loop first (CR 103.4). The first
        // prompt is always a MulliganCommand for one of the players.
        prompts.Should().NotBeEmpty();
        prompts[0].ExpectedKinds.Should().Contain(nameof(MulliganCommand));
    }

    [Fact]
    public async Task StartFullGameAsync_CannotBeStartedTwice()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        await facade.StartFullGameAsync(maxTurns: 1);

        var act = () => facade.StartFullGameAsync(maxTurns: 1);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartFullGameAsync_FullGameTaskExposed()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        await facade.StartFullGameAsync(maxTurns: 1);

        facade.FullGameTask.Should().NotBeNull();
    }
}
