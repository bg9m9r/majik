using FluentAssertions;
using Majik.Bot;
using Majik.Bot.Tests.Integration.Helpers;
using Majik.Core.Api;
using Xunit;

namespace Majik.Bot.Tests.Integration;

public class BotVsRandomAgentTests
{
    [Fact(Skip = "v1 placeholder decks contain unimplemented cards - win-rate test is meaningless until real deck lists land. Re-enable once user provides deck lists.")]
    public async Task Burn_BeatsRandom_AtLeast70Pct()
    {
        int wins = 0;
        for (int i = 0; i < 20; i++)
        {
            var facade = GameFacade.Create(
                aliceName: "Burn-Bot",
                bobName: "Random",
                aliceDeck: DeckLoader.Load("Burn"),
                bobDeck:   DeckLoader.Load("Burn"));

            facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, new BotConfig("Burn", RandomSeed: i)));
            facade.ReplaceBobAgent(new RandomAgent(seed: i + 1000));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await facade.StartFullGameAsync(maxTurns: 30, ct: cts.Token);
            var result = await facade.FullGameTask!;

            if (ReferenceEquals(result.Winner, facade.Alice)) wins++;
        }
        wins.Should().BeGreaterThanOrEqualTo(14);
    }
}
