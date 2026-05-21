using FluentAssertions;
using Majik.Bot;
using Majik.Bot.Tests.Integration.Helpers;
using Majik.Core.Api;
using Xunit;

namespace Majik.Bot.Tests.Integration;

public class BotVsBotGameTests
{
    [Fact]
    public async Task BurnVsBoros_PlaysGame_NoCrash()
    {
        var facade = GameFacade.Create(
            aliceName: "Burn-Bot",
            bobName: "Boros-Bot",
            aliceDeck: DeckLoader.Load("Burn"),
            bobDeck:   DeckLoader.Load("BorosEnergy"));

        facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, new BotConfig("Burn",        RandomSeed: 1)));
        facade.ReplaceBobAgent(  new BotPlayerAgent(facade.Bob,   new BotConfig("BorosEnergy", RandomSeed: 2)));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await facade.StartFullGameAsync(maxTurns: 20, ct: cts.Token);
        await facade.FullGameTask!;
        facade.FullGameTask!.IsCompletedSuccessfully.Should().BeTrue();
    }
}
