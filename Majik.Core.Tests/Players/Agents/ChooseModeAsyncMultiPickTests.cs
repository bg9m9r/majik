using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Tests.Players.Agents;

/// <summary>
/// CR 700.2d — multi-pick "Choose N —" prompt. Covers the new
/// list-returning <see cref="IPlayerAgent.ChooseModeAsync(IReadOnlyList{string}, BotIntent, int, System.Threading.CancellationToken)"/>
/// overload:
///   - Default-impl on agents that don't override returns 0..requiredCount-1
///     (legacy posture). <see cref="ScriptedAgent"/> uses the interface
///     default since it pre-dates this prompt.
///   - Caps requiredCount at modes.Count (won't synthesise out-of-range
///     indices).
///   - Empty modes / non-positive requiredCount returns an empty list.
///   - <see cref="HeuristicBotAgent"/>'s intent-aware override returns
///     picks in printed order (so EffectFactory.ModeIndexes can index
///     sequentially).
/// </summary>
public class ChooseModeAsyncMultiPickTests
{
    [Fact]
    public async Task DefaultImpl_PicksFirstRequiredCount_Indices()
    {
        IPlayerAgent agent = new ScriptedAgent();
        var modes = new[] { "A", "B", "C", "D" };

        var picks = await agent.ChooseModeAsync(modes, BotIntent.None, requiredCount: 2);

        picks.Should().BeEquivalentTo(new[] { 0, 1 },
            because: "default-impl returns 0..requiredCount-1 (deterministic legacy posture)");
    }

    [Fact]
    public async Task DefaultImpl_RequiredCount_CappedAtModesCount()
    {
        IPlayerAgent agent = new ScriptedAgent();
        var modes = new[] { "A", "B" };

        var picks = await agent.ChooseModeAsync(modes, BotIntent.None, requiredCount: 5);

        picks.Should().HaveCount(2,
            because: "requiredCount is capped at modes.Count");
        picks.Should().BeEquivalentTo(new[] { 0, 1 });
    }

    [Fact]
    public async Task DefaultImpl_EmptyModes_ReturnsEmpty()
    {
        IPlayerAgent agent = new ScriptedAgent();

        var picks = await agent.ChooseModeAsync(System.Array.Empty<string>(), BotIntent.None, requiredCount: 1);

        picks.Should().BeEmpty();
    }

    [Fact]
    public async Task DefaultImpl_NonPositiveRequiredCount_ReturnsEmpty()
    {
        IPlayerAgent agent = new ScriptedAgent();
        var modes = new[] { "A", "B" };

        (await agent.ChooseModeAsync(modes, BotIntent.None, requiredCount: 0)).Should().BeEmpty();
        (await agent.ChooseModeAsync(modes, BotIntent.None, requiredCount: -3)).Should().BeEmpty();
    }

    [Fact]
    public async Task DefaultImpl_PicksAreDistinct()
    {
        IPlayerAgent agent = new ScriptedAgent();
        var modes = new[] { "A", "B", "C" };

        var picks = await agent.ChooseModeAsync(modes, BotIntent.None, requiredCount: 3);

        picks.Distinct().Count().Should().Be(picks.Count,
            because: "CR 700.2d — each chosen mode is distinct");
    }

    [Fact]
    public async Task HeuristicBot_ReturnsRequiredCount_DistinctIndices_InPrintedOrder()
    {
        IPlayerAgent agent = new HeuristicBotAgent();
        var modes = new[]
        {
            "Counter target spell.",
            "Draw a card.",
            "Destroy target creature.",
            "Gain 3 life.",
        };

        var picks = await agent.ChooseModeAsync(modes, BotIntent.Removal, requiredCount: 2);

        picks.Should().HaveCount(2);
        picks.Distinct().Count().Should().Be(2,
            because: "CR 700.2d — distinct modes");
        picks.Should().BeInAscendingOrder(
            because: "heuristic returns picks in printed order so effect application is deterministic");
        foreach (var idx in picks)
        {
            (idx >= 0 && idx < modes.Length).Should().BeTrue();
        }
    }

    [Fact]
    public async Task HeuristicBot_SingleMode_SingleIndex()
    {
        IPlayerAgent agent = new HeuristicBotAgent();
        var modes = new[] { "Counter target spell.", "Destroy target creature.", "Draw a card." };

        var picks = await agent.ChooseModeAsync(modes, BotIntent.Counter, requiredCount: 1);

        picks.Should().HaveCount(1);
        picks[0].Should().BeInRange(0, modes.Length - 1);
    }

    [Fact]
    public async Task HeuristicBot_EmptyModes_ReturnsEmpty()
    {
        IPlayerAgent agent = new HeuristicBotAgent();

        var picks = await agent.ChooseModeAsync(System.Array.Empty<string>(), BotIntent.None, requiredCount: 1);

        picks.Should().BeEmpty();
    }
}
