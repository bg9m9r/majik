using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Majik.Bot;
using Majik.Bot.Tests.Integration.Helpers;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Random;

namespace Majik.Bot.Tests.Integration.Fuzz;

/// <summary>
/// Runs one seeded bot-vs-bot game with <see cref="GameInvariantObserver"/> attached
/// under a wall-clock timeout and returns a <see cref="FuzzResult"/>.
/// </summary>
public static class FuzzGameRunner
{
    public static async Task<FuzzResult> RunOnce(
        string deckA, string deckB, int seed, int maxTurns, TimeSpan timeout)
    {
        var facade = GameFacade.Create(
            aliceName: $"{deckA}-Bot",
            bobName: $"{deckB}-Bot",
            aliceDeck: DeckLoader.Load(deckA),
            bobDeck: DeckLoader.Load(deckB),
            cardRepo: new EmbeddedCardRepository());

        facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, new BotConfig(deckA, RandomSeed: seed * 2 + 1)));
        facade.ReplaceBobAgent(new BotPlayerAgent(facade.Bob, new BotConfig(deckB, RandomSeed: seed * 2 + 2)));

        using var observer = new GameInvariantObserver(
            facade.EventBus,
            new[] { facade.Alice, facade.Bob },
            () => facade.Triggers.CreatureEtbTriggerSuppressionCount);

        bool timedOut = false;
        bool reachedCap = false;
        string? winner = null;
        int turns = 0;
        InvariantViolation? crashViolation = null;

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await facade.StartFullGameAsync(maxTurns: maxTurns, ct: cts.Token, rng: new GameRandom(seed));
            var gameResult = await facade.FullGameTask!;
            // GameDriver.GameResult has: int TurnsPlayed, Player? Winner, Player? StartingPlayer
            winner = gameResult.Winner?.Name;
            turns = gameResult.TurnsPlayed;
            reachedCap = turns >= maxTurns && winner is null;
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
        }
        catch (System.Exception ex)
        {
            // Engine threw an unhandled exception — capture as a crash violation.
            // The seed alone fully reproduces the run; the snapshot dump below also fires.
            crashViolation = new InvariantViolation(
                "EngineCrash",
                $"{ex.GetType().Name}: {ex.Message}",
                turns, "GameEnd");
        }

        observer.RunFinalChecks(turn: turns, phase: "GameEnd", winnerName: winner, reachedTurnCap: reachedCap);

        if (observer.Violations.Count > 0 || timedOut || crashViolation is not null)
        {
            try
            {
                var snapshot = facade.SaveSnapshot();
                var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "majik-fuzz");
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, $"fuzz-{deckA}-{deckB}-seed{seed}.json");
                var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
                System.IO.File.WriteAllText(path, json);
                System.Console.WriteLine($"[fuzz] repro snapshot written: {path}");
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"[fuzz] snapshot dump failed: {ex.Message}");
            }
        }

        var allViolations = observer.Violations.ToList();
        if (crashViolation is not null)
            allViolations.Insert(0, crashViolation);

        return new FuzzResult(seed, deckA, deckB, turns, winner, timedOut, reachedCap, allViolations);
    }
}
