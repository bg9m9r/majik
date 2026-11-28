using Majik.Core.Api.BotReplay;
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
        var mismatches = new List<string>();
        var checkedGrants = 0;
        var primed = false;
        // Observed at the bot's actual priority decision (CR 117.1: the holder
        // has priority once SBAs ran and triggers are on the stack, CR 117.5).
        // RecordingPlayerAgent's record callback runs after the bot chose but
        // before the loop applies the choice, i.e. inside the decision window.
        Func<BotDecisionRecord, Task> Check(Func<Majik.Core.Players.Player> seat) => rec =>
        {
            if (rec.Kind != BotDecisionKind.Priority) return Task.CompletedTask;
            Majik.Core.Api.Dtos.GameStateDto? alice = null, bob = null;
            EngineFlow.RunExternal(() =>
                (alice, bob) = Task.Run(() => (facade.GetStateFor(facade.Alice.Id), facade.GetStateFor(facade.Bob.Id))).Result);
            if (!primed) { primed = true; return Task.CompletedTask; } // first foreign read registers both views
            checkedGrants++;
            var holder = seat();
            if (alice!.PriorityPlayerId != holder.Id) mismatches.Add($"alice view: {alice.PriorityPlayerId} != {holder.Name}");
            if (bob!.PriorityPlayerId != holder.Id) mismatches.Add($"bob view: {bob.PriorityPlayerId} != {holder.Name}");
            return Task.CompletedTask;
        };
        facade.ReplaceAliceAgent(new RecordingPlayerAgent(new HeuristicBotAgent(cardRepository: Repo), Check(() => facade.Alice)));
        facade.ReplaceBobAgent(new RecordingPlayerAgent(new HeuristicBotAgent(cardRepository: Repo), Check(() => facade.Bob)));

        await facade.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 6, rng: new GameRandom(5));
        await facade.FullGameTask!.WaitAsync(TimeSpan.FromSeconds(60));

        checkedGrants.Should().BeGreaterThan(10);
        mismatches.Should().BeEmpty();
        facade.GetState().PriorityPlayerId.Should().BeNull("nobody holds priority once the game is over");
    }

    [Fact]
    public async Task OnceTheDecisionIsMade_ThePublishedSnapshotNoLongerNamesTheDecider()
    {
        // CR 117.3c — passing ends the decision; until the next player is
        // granted priority nobody holds it, and an off-engine reader must see that.
        using var ids = DeterministicIdScope.Push(new DeterministicIdSource(5));
        var facade = GameFacade.Create("Alice", "Bob", RealDeck("Burn"), RealDeck("BorosEnergy"), cardRepo: Repo);
        facade.ReplaceAliceAgent(new HeuristicBotAgent(cardRepository: Repo));
        facade.ReplaceBobAgent(new HeuristicBotAgent(cardRepository: Repo));
        var stale = new List<string>();
        var checkedPasses = 0;
        var primed = false;
        facade.EventBus.Subscribe<PriorityPassedEvent>(e =>
        {
            Majik.Core.Api.Dtos.GameStateDto? alice = null, bob = null;
            EngineFlow.RunExternal(() =>
                (alice, bob) = Task.Run(() => (facade.GetStateFor(facade.Alice.Id), facade.GetStateFor(facade.Bob.Id))).Result);
            if (!primed) { primed = true; return; } // the first foreign read registers both views
            checkedPasses++;
            if (alice!.PriorityPlayerId != null) stale.Add($"alice view still names {alice.PriorityPlayerId} after {e.Player.Name} passed");
            if (bob!.PriorityPlayerId != null) stale.Add($"bob view still names {bob.PriorityPlayerId} after {e.Player.Name} passed");
        });

        await facade.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 4, rng: new GameRandom(5));
        await facade.FullGameTask!.WaitAsync(TimeSpan.FromSeconds(60));

        checkedPasses.Should().BeGreaterThan(10);
        stale.Should().BeEmpty();
    }

    [Fact]
    public void AThrowingDecidingPlayerSubscriber_DoesNotEscapeIntoThePriorityLoop()
    {
        var deck = Enumerable.Range(0, 20).Select(_ => (ICard)new Land("Forest")).ToList();
        var facade = GameFacade.Create("Alice", "Bob", deck, deck.ToList());
        var priority = facade.Priority;
        var after = 0;
        priority.DecidingPlayerChanged += _ => throw new InvalidOperationException("observer bug");
        priority.DecidingPlayerChanged += _ => after++;

        var act = () => { priority.SetDecidingPlayer(facade.Alice); priority.SetDecidingPlayer(null); };

        act.Should().NotThrow();
        after.Should().Be(2, "one throwing observer must not starve the others");
        priority.DecidingPlayer.Should().BeNull();
    }

    [Fact]
    public async Task WhileATriggerIsPutOnTheStack_BeforePriorityIsGranted_NobodyHoldsPriority()
    {
        // CR 117.5 / 603.3 — before a player receives priority, SBAs run and
        // pending triggers are put on the stack; the trigger's controller (here
        // Bob, while Alice is about to get priority) is prompted for targets.
        // Nobody holds priority in that window, so a client gating input on
        // PriorityPlayerId must not see Alice as the holder.
        var deck = Enumerable.Range(0, 20).Select(_ => (ICard)new Land("Forest")).ToList();
        var facade = GameFacade.Create("Alice", "Bob", deck, deck.ToList());
        var source = new Creature("Pinger", "{R}", 1, 1);
        source.SetOwner(facade.Bob); source.SetController(facade.Bob);
        facade.Bob.Zones.Battlefield.AddCard(source); source.SetZone(Majik.Core.Zones.ZoneType.Battlefield);
        var trigger = new Majik.Core.Abilities.TriggeredAbility(source, facade.Bob,
            Majik.Core.Abilities.Triggers.OnEnterBattlefieldSelf(source),
            targetRequests: new[] { new TargetRequest("target player", 1, 1, new object[] { facade.Alice, facade.Bob }) });
        facade.Triggers.EnqueuePending(trigger);

        var targetPromptHolders = new List<Guid?>();
        var priorityMismatches = new List<string>();
        using var sub = facade.SubscribePrompts(p =>
        {
            if (p.ExpectedKinds.Contains(nameof(ChooseTargetsCommand)))
            {
                lock (targetPromptHolders) targetPromptHolders.Add(facade.GetStateFor(facade.Alice.Id)!.PriorityPlayerId);
                _ = facade.SubmitAsync(new ChooseTargetsCommand(new[] { facade.Alice.Id }) { PlayerId = p.PlayerId });
                return;
            }
            if (p.ExpectedKinds.Contains(nameof(MulliganCommand)))
            {
                _ = facade.SubmitAsync(new MulliganCommand(Keep: true) { PlayerId = p.PlayerId });
                return;
            }
            if (!p.ExpectedKinds.Contains(nameof(PassPriorityCommand))) return;
            if (facade.GetState().PriorityPlayerId != p.PlayerId) lock (priorityMismatches) priorityMismatches.Add(p.PlayerId.ToString());
            _ = facade.SubmitAsync(new PassPriorityCommand { PlayerId = p.PlayerId });
        });

        // The full-game path drains triggers through the agent-aware async
        // drain (the single-round StartAsync path auto-targets).
        await facade.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 1, rng: new GameRandom(1));
        await facade.FullGameTask!.WaitAsync(TimeSpan.FromSeconds(30));

        targetPromptHolders.Should().ContainSingle().Which.Should().BeNull(
            "the engine is putting a trigger on the stack; nobody holds priority");
        priorityMismatches.Should().BeEmpty();
    }
}