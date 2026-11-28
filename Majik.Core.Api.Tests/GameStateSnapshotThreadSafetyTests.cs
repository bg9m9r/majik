using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Core.Api.Tests;

/// <summary>
/// <see cref="GameFacade.GetState"/> / <see cref="GameFacade.GetStateFor"/>
/// called from a thread other than the engine's must never walk live game
/// objects: doing so threw in the reader AND corrupted the running game (the
/// reader's layer Compute wrote ContinuousEffectsService caches the engine was
/// using). Off-engine callers get the immutable snapshot the engine published
/// at its last quiescent point (before every prompt, at every priority grant /
/// step start for views someone reads, and at game end).
/// </summary>
[Collection(FuzzCollection.Name)] // serial: full games share static registry state
public sealed class GameStateSnapshotThreadSafetyTests
{
    private static readonly EmbeddedCardRepository Repo = new();
    private readonly ITestOutputHelper _out;

    public GameStateSnapshotThreadSafetyTests(ITestOutputHelper output) => _out = output;

    private static List<ICard> RealDeck(string archetype) =>
        Majik.Bot.Decks.BotDeckCatalog.Get(archetype)
            .Select(n => DeckCardShellBuilder.Build(Repo.GetByName(n)
                ?? throw new InvalidOperationException($"not in seed: {n}")))
            .ToList();

    private sealed record Outcome(string? Winner, int Turns, string? Fault);

    public enum Reader { None, ForeignThreads, HandlerTaskRun, HandlerAwaitYield }

    private static Task<(Outcome Outcome, int Reads, List<Exception> ReaderErrors)> PlayAsync(
        string aliceDeck, string bobDeck, int seed, bool withReader)
        => PlayAsync(aliceDeck, bobDeck, seed, withReader ? Reader.ForeignThreads : Reader.None);

    private static async Task<(Outcome Outcome, int Reads, List<Exception> ReaderErrors)> PlayAsync(
        string aliceDeck, string bobDeck, int seed, Reader mode)
    {
        var withReader = mode == Reader.ForeignThreads;
        using var ids = DeterministicIdScope.Push(new DeterministicIdSource(seed));
        var facade = GameFacade.Create("Alice", "Bob", RealDeck(aliceDeck), RealDeck(bobDeck), cardRepo: Repo);
        facade.ReplaceAliceAgent(new HeuristicBotAgent(cardRepository: Repo));
        facade.ReplaceBobAgent(new HeuristicBotAgent(cardRepository: Repo));

        var errors = new List<Exception>();
        var reads = 0;
        using var stop = new CancellationTokenSource();
        Task reader = Task.CompletedTask;
        if (withReader)
        {
            var viewers = new Guid?[] { null, facade.Alice.Id, facade.Bob.Id };
            // Two readers hammering every view, like a UI thread plus a
            // network thread.
            reader = Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    foreach (var viewer in viewers)
                    {
                        try
                        {
                            var state = viewer is { } v ? facade.GetStateFor(v) : facade.GetState();
                            state.Should().NotBeNull();
                        }
                        catch (Exception ex)
                        {
                            lock (errors) errors.Add(ex);
                        }
                        Interlocked.Increment(ref reads);
                    }
                }
            })));
        }

        // Work started BY an event handler (a Task.Run, or an await
        // continuation) must not inherit the engine-flow tag: it reads the
        // published snapshot like any other foreign caller.
        var outstanding = 0;
        IDisposable? handlerSub = null;
        void HandlerRead()
        {
            try
            {
                if (facade.IsOnEngineFlow)
                    throw new InvalidOperationException("handler-started work was treated as engine flow");
                facade.GetState().Should().NotBeNull();
                facade.GetStateFor(facade.Alice.Id).Should().NotBeNull();
                facade.GetStateFor(facade.Bob.Id).Should().NotBeNull();
            }
            catch (Exception ex)
            {
                lock (errors) errors.Add(ex);
            }
            Interlocked.Increment(ref reads);
        }
        if (mode == Reader.HandlerTaskRun)
        {
            handlerSub = facade.Subscribe(ev =>
            {
                if (facade.IsOnEngineFlow)
                    lock (errors) errors.Add(new InvalidOperationException("synchronous handler ran on the engine flow"));
                Interlocked.Increment(ref outstanding);
                _ = Task.Run(() => { HandlerRead(); Interlocked.Decrement(ref outstanding); });
            });
        }
        else if (mode == Reader.HandlerAwaitYield)
        {
            handlerSub = facade.SubscribeEnvelopes(async _ =>
            {
                Interlocked.Increment(ref outstanding);
                await Task.Yield();
                HandlerRead();
                Interlocked.Decrement(ref outstanding);
            });
        }

        string? fault = null;
        GameDriver.GameResult? result = null;
        try
        {
            await facade.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 12, rng: new GameRandom(seed),
                logicalClock: new LogicalClock());
            result = await facade.FullGameTask!.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (Exception ex)
        {
            fault = ex.GetType().Name;
        }
        finally
        {
            stop.Cancel();
            await reader;
            handlerSub?.Dispose();
            var drain = System.Diagnostics.Stopwatch.StartNew();
            while (Volatile.Read(ref outstanding) > 0 && drain.Elapsed < TimeSpan.FromSeconds(30))
                await Task.Delay(5);
        }
        return (new Outcome(result?.Winner?.Name, result?.TurnsPlayed ?? -1, fault), reads, errors);
    }

    public static IEnumerable<object[]> Pairs => new[]
    {
        new object[] { "Burn", "BorosEnergy" },
        new object[] { "Prowess", "Burn" },
        new object[] { "BorosEnergy", "Prowess" },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task ForeignThreadReader_NeverThrows_AndNeverChangesTheGame(string aliceDeck, string bobDeck)
    {
        var readerErrors = new List<Exception>();
        var reads = 0;
        foreach (var seed in Enumerable.Range(100, 10))
        {
            var baseline = await PlayAsync(aliceDeck, bobDeck, seed, withReader: false);
            var stressed = await PlayAsync(aliceDeck, bobDeck, seed, withReader: true);
            _out.WriteLine($"{aliceDeck} v {bobDeck} seed {seed}: baseline {baseline.Outcome}, " +
                           $"with reader {stressed.Outcome}, {stressed.Reads} reads, {stressed.ReaderErrors.Count} reader errors");
            readerErrors.AddRange(stressed.ReaderErrors);
            reads += stressed.Reads;
            stressed.Outcome.Should().Be(baseline.Outcome, $"reading state must not change the game (seed {seed})");
        }
        reads.Should().BeGreaterThan(0);
        readerErrors.Should().BeEmpty("an off-engine read must never touch live game objects; first: "
            + readerErrors.FirstOrDefault());
    }

    [Theory]
    [InlineData(Reader.HandlerTaskRun)]
    [InlineData(Reader.HandlerAwaitYield)]
    public async Task WorkStartedByAnEventHandler_IsNotEngineFlow_AndNeverChangesTheGame(Reader mode)
    {
        var readerErrors = new List<Exception>();
        var reads = 0;
        foreach (var (aliceDeck, bobDeck) in new[] { ("Burn", "BorosEnergy"), ("Prowess", "Burn"), ("BorosEnergy", "Prowess") })
        foreach (var seed in Enumerable.Range(100, 5))
        {
            var baseline = await PlayAsync(aliceDeck, bobDeck, seed, Reader.None);
            var stressed = await PlayAsync(aliceDeck, bobDeck, seed, mode);
            _out.WriteLine($"{mode} {aliceDeck} v {bobDeck} seed {seed}: baseline {baseline.Outcome}, " +
                           $"with handler reads {stressed.Outcome}, {stressed.Reads} reads, {stressed.ReaderErrors.Count} errors");
            readerErrors.AddRange(stressed.ReaderErrors);
            reads += stressed.Reads;
            stressed.Outcome.Should().Be(baseline.Outcome, $"handler-started reads must not change the game (seed {seed})");
        }
        reads.Should().BeGreaterThan(0);
        readerErrors.Should().BeEmpty("first: " + readerErrors.FirstOrDefault());
    }

    [Fact]
    public async Task EngineCallees_StillReadOnTheEngineFlow()
    {
        // Internal engine callees — here a handler on the engine's own bus,
        // the surface triggers / SBAs / bots are driven through — keep live
        // reads; only the facade's public subscriber fan-out is untagged.
        var deck = Enumerable.Range(0, 20).Select(_ => (ICard)new Land("Forest")).ToList();
        var facade = GameFacade.Create("Alice", "Bob", deck, deck.Select(_ => (ICard)new Land("Mountain")).ToList());
        facade.ReplaceAliceAgent(new HeuristicBotAgent());
        facade.ReplaceBobAgent(new HeuristicBotAgent());
        int engineCalls = 0, offFlow = 0, publicCalls = 0, publicOnFlow = 0;
        facade.EventBus.Subscribe<Majik.Core.Events.StepStartedEvent>(_ =>
        {
            engineCalls++;
            if (!facade.IsOnEngineFlow) offFlow++;
            else facade.GetState().Should().NotBeNull();
        });
        using var sub = facade.Subscribe(_ => { publicCalls++; if (facade.IsOnEngineFlow) publicOnFlow++; });
        await facade.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 2, rng: new GameRandom(3));
        await facade.FullGameTask!.WaitAsync(TimeSpan.FromSeconds(30));
        engineCalls.Should().BeGreaterThan(0);
        offFlow.Should().Be(0);
        publicCalls.Should().BeGreaterThan(0);
        publicOnFlow.Should().Be(0);
    }
    private static RemoteAgent Seat(GameFacade facade, string field) =>
        (RemoteAgent)typeof(GameFacade)
            .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(facade)!;

    [Fact]
    public async Task SnapshotReadOnAPrompt_ReflectsTheStateThePromptWasRaisedIn()
    {
        // Alice casts a spell; Bob is then prompted (priority, spell on the
        // stack). A reader on another thread, handed that prompt, must see the
        // spell on the stack — the snapshot is published before the prompt.
        var aliceDeck = Enumerable.Range(0, 20).Select(_ => (ICard)new Creature("Grizzly Bears", "1G", 2, 2)).ToList();
        var bobDeck = Enumerable.Range(0, 20).Select(_ => (ICard)new Land("Mountain")).ToList();
        var facade = GameFacade.Create("Alice", "Bob", aliceDeck, bobDeck);
        var forestA = new Land("Forest");
        var forestB = new Land("Forest");
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        foreach (var c in new Card[] { forestA, forestB })
        {
            c.SetOwner(facade.Alice); c.SetController(facade.Alice);
            facade.Alice.Zones.Battlefield.AddCard(c); c.SetZone(Majik.Core.Zones.ZoneType.Battlefield);
            ((Land)c).AddAbility(new Majik.Core.Abilities.ManaAbility((Land)c, facade.Alice,
                Majik.Core.ValueObjects.ManaCost.Parse("G")));
        }
        bear.SetOwner(facade.Alice); bear.SetController(facade.Alice);
        facade.Alice.Zones.Hand.AddCard(bear); bear.SetZone(Majik.Core.Zones.ZoneType.Hand);

        var bobPrompts = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
        using var sub = facade.SubscribePrompts(p => { if (p.PlayerId == facade.Bob.Id) bobPrompts.Writer.TryWrite(p); });
        await facade.StartAsync();

        await facade.SubmitAsync(new CastSpellCommand(bear.InstanceId, Array.Empty<Guid>(), null, null) { PlayerId = facade.Alice.Id });
        if (Seat(facade, "_aliceAgent").ExpectedCommandKinds?.Contains(typeof(ChooseManaCommand)) == true)
        {
            await facade.SubmitAsync(new ChooseManaCommand(Array.Empty<Guid>()) { PlayerId = facade.Alice.Id });
        }
        // CR 117.3c — the caster gets priority first; passing hands it to Bob.
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = facade.Alice.Id });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var prompt = await bobPrompts.Reader.ReadAsync(cts.Token);
        prompt.ExpectedKinds.Should().Contain(nameof(PassPriorityCommand));

        var (state, again) = await Task.Run(() => (facade.GetStateFor(facade.Bob.Id)!, facade.GetStateFor(facade.Bob.Id)!));
        state.Stack.Should().ContainSingle().Which.Description.Should().Be("Grizzly Bears");
        again.Should().BeSameAs(state, "an off-engine read returns the published immutable snapshot");
    }

    [Fact]
    public async Task AfterTheGameEnds_TheSnapshotShowsTheLoser()
    {
        var deck = Enumerable.Range(0, 20).Select(_ => (ICard)new Land("Forest")).ToList();
        var facade = GameFacade.Create("Alice", "Bob", deck, deck.Select(_ => (ICard)new Land("Mountain")).ToList());
        var prompts = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
        using var sub = facade.SubscribePrompts(p => prompts.Writer.TryWrite(p));
        await facade.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 10, rng: new GameRandom(4));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await prompts.Reader.ReadAsync(cts.Token);

        await facade.ConcedeAsync(facade.Alice.Id);

        var states = await Task.Run(() => new[]
        {
            facade.GetState(), facade.GetStateFor(facade.Alice.Id)!, facade.GetStateFor(facade.Bob.Id)!,
        });
        foreach (var state in states)
        {
            state.Players.Single(p => p.Id == facade.Alice.Id).HasLost.Should().BeTrue();
            state.Players.Single(p => p.Id == facade.Bob.Id).HasLost.Should().BeFalse();
        }
    }
}
