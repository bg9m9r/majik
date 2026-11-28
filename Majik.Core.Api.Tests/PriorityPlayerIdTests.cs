using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// CR 117.1 / 117.3 — <see cref="Dtos.GameStateDto.PriorityPlayerId"/> names the
/// player who holds priority, in the published snapshot every off-engine reader
/// sees, for both seats' views, whether that player is a human (remote) seat
/// or a bot.
/// </summary>
[Collection(FuzzCollection.Name)]
public sealed class PriorityPlayerIdTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    private static List<ICard> RealDeck(string archetype) =>
        Majik.Bot.Decks.BotDeckCatalog.Get(archetype)
            .Select(n => DeckCardShellBuilder.Build(Repo.GetByName(n)!))
            .ToList();

    [Fact]
    public async Task AtAHumanSeatsPriorityPrompt_BothViewsNameThatSeat()
    {
        var deck = Enumerable.Range(0, 20).Select(_ => (ICard)new Land("Forest")).ToList();
        var facade = GameFacade.Create("Alice", "Bob", deck, deck.ToList());
        var mismatches = new List<string>();
        var seen = 0;
        using var sub = facade.SubscribePrompts(p =>
        {
            if (!p.ExpectedKinds.Contains(nameof(PassPriorityCommand))) return;
            Interlocked.Increment(ref seen);
            foreach (var viewer in new[] { facade.Alice.Id, facade.Bob.Id })
            {
                var holder = facade.GetStateFor(viewer)!.PriorityPlayerId;
                if (holder != p.PlayerId) lock (mismatches) mismatches.Add($"viewer {viewer}: {holder} != {p.PlayerId}");
            }
            if (facade.GetState().PriorityPlayerId != p.PlayerId) lock (mismatches) mismatches.Add("spectator");
            _ = facade.SubmitAsync(new PassPriorityCommand { PlayerId = p.PlayerId });
        });

        await facade.StartAsync();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!facade.IsRoundComplete && DateTime.UtcNow < deadline) await Task.Delay(5);

        seen.Should().BeGreaterThan(1);
        mismatches.Should().BeEmpty();
    }

    [Fact]
    public async Task WhileABotHoldsPriority_ThePublishedSnapshotNamesIt_ForBothViews()
    {
        using var ids = DeterministicIdScope.Push(new DeterministicIdSource(5));
        var facade = GameFacade.Create("Alice", "Bob", RealDeck("Burn"), RealDeck("BorosEnergy"), cardRepo: Repo);
        facade.ReplaceAliceAgent(new HeuristicBotAgent(cardRepository: Repo));
        facade.ReplaceBobAgent(new HeuristicBotAgent(cardRepository: Repo));
        var mismatches = new List<string>();
        var checkedGrants = 0;
        var primed = false;
        // Registered after the facade's own publish handler, so the snapshot
        // for this grant is already published when this runs.
        facade.EventBus.Subscribe<PriorityReceivedEvent>(ev =>
        {
            Majik.Core.Api.Dtos.GameStateDto? alice = null, bob = null;
            EngineFlow.RunExternal(() =>
                (alice, bob) = Task.Run(() => (facade.GetStateFor(facade.Alice.Id), facade.GetStateFor(facade.Bob.Id))).Result);
            if (!primed) { primed = true; return; } // first foreign read registers both views
            checkedGrants++;
            if (alice!.PriorityPlayerId != ev.Player.Id) mismatches.Add($"alice view: {alice.PriorityPlayerId} != {ev.Player.Name}");
            if (bob!.PriorityPlayerId != ev.Player.Id) mismatches.Add($"bob view: {bob.PriorityPlayerId} != {ev.Player.Name}");
        });

        await facade.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 6, rng: new GameRandom(5));
        await facade.FullGameTask!.WaitAsync(TimeSpan.FromSeconds(60));

        checkedGrants.Should().BeGreaterThan(10);
        mismatches.Should().BeEmpty();
        facade.GetState().PriorityPlayerId.Should().BeNull("nobody holds priority once the game is over");
    }
}
