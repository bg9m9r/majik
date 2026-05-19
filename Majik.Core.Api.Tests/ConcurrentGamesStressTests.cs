using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Xunit;

public class ConcurrentGamesStressTests
{
    [Fact]
    public async Task Create100GamesInParallel_AllUnique_NoExceptions()
    {
        var registry = new GameRegistry();

        var games = await Task.WhenAll(
            Enumerable.Range(0, 100).Select(i => Task.Run(() =>
                registry.Create($"A{i}", $"B{i}"))));

        games.Should().HaveCount(100);
        games.Select(g => g.GameId).Distinct().Should().HaveCount(100);
        registry.Count.Should().Be(100);
    }

    [Fact]
    public async Task ManyGames_SubmitInParallel_NoCrossTalk()
    {
        var registry = new GameRegistry();
        var facades = Enumerable.Range(0, 30)
            .Select(i => registry.Create($"A{i}", $"B{i}"))
            .ToList();

        // Start each game.
        await Task.WhenAll(facades.Select(f => f.StartAsync()));

        // Capture each game's pre-submit active player so we can verify
        // the log entry matches even if priority advances later.
        var expectedPlayers = facades
            .ToDictionary(f => f, f => f.GetState().ActivePlayerId);

        await Task.WhenAll(facades.Select(f => Task.Run(async () =>
            await f.SubmitAsync(new PassPriorityCommand { PlayerId = expectedPlayers[f] }))));

        // Each facade should have exactly one logged action — its own pass.
        foreach (var f in facades)
        {
            f.Log.Count.Should().Be(1);
            f.Log.Actions[0].Command.PlayerId.Should().Be(expectedPlayers[f]);
        }
    }
}
