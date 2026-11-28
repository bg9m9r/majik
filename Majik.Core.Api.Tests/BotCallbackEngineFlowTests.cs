using FluentAssertions;
using Majik.Bot;
using Majik.Bot.Diagnostics;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Random;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Callbacks a consumer hands to a bot (the "thinking" callback, a decision
/// sink) are external code: they, and anything they schedule, must never be
/// treated as the engine flow (which reads live game objects without a lock).
/// </summary>
[Collection(FuzzCollection.Name)]
public sealed class BotCallbackEngineFlowTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    private static List<ICard> RealDeck(string archetype) =>
        Majik.Bot.Decks.BotDeckCatalog.Get(archetype)
            .Select(n => DeckCardShellBuilder.Build(Repo.GetByName(n)!))
            .ToList();

    private sealed class ProbeSink(Func<bool> probe, List<Task<bool>> into) : IBotDecisionSink
    {
        public void Record(BotDecision decision)
        {
            lock (into) into.Add(Task.Run(probe));
        }
    }

    [Fact]
    public async Task ThinkingCallbackAndDecisionSink_AndWorkTheyStart_AreNotEngineFlow()
    {
        using var ids = DeterministicIdScope.Push(new DeterministicIdSource(21));
        var facade = GameFacade.Create("Alice", "Bob", RealDeck("Burn"), RealDeck("BorosEnergy"), cardRepo: Repo);
        var syncOnFlow = 0;
        var scheduled = new List<Task<bool>>();
        Action<bool> thinking = _ =>
        {
            if (facade.IsOnEngineFlow) Interlocked.Increment(ref syncOnFlow);
            lock (scheduled) scheduled.Add(Task.Run(() => facade.IsOnEngineFlow));
        };
        var sink = new ProbeSink(() => facade.IsOnEngineFlow, scheduled);
        facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, new BotConfig("Burn", RandomSeed: 1, DecisionSink: sink), thinking));
        facade.ReplaceBobAgent(new BotPlayerAgent(facade.Bob, new BotConfig("BorosEnergy", RandomSeed: 2, DecisionSink: sink), thinking));

        await facade.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 6, rng: new GameRandom(21));
        await facade.FullGameTask!.WaitAsync(TimeSpan.FromSeconds(60));

        Task<bool>[] all;
        lock (scheduled) all = scheduled.ToArray();
        var results = await Task.WhenAll(all);
        results.Should().NotBeEmpty();
        results.Should().OnlyContain(onFlow => !onFlow, "work a bot callback schedules is a foreign caller");
        syncOnFlow.Should().Be(0, "the callback itself runs off the engine flow");
    }
}
