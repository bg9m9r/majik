using System.Threading.Channels;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// CR 104.3a — "A player can concede the game at any time. A player who
/// concedes leaves the game immediately. That player loses the game."
/// <see cref="GameFacade.ConcedeAsync"/> ends a running full game through the
/// engine's normal loss path: the conceder is marked lost, a
/// <c>PlayerLostEvent</c> is published, the driver halts at its game-over
/// gate and <see cref="GameFacade.FullGameTask"/> completes (never faults)
/// with the opponent as winner (CR 104.2a). Nothing is left awaiting.
/// </summary>
[Collection(FuzzCollection.Name)] // serial: shares the full-game driver model
public sealed class GameFacadeConcedeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static List<ICard> LandDeck(string name, int count = 20)
        => Enumerable.Range(0, count).Select(_ => (ICard)new Land(name)).ToList();

    private sealed class Harness
    {
        public required GameFacade Facade { get; init; }
        public required Channel<PromptDto> Prompts { get; init; }
        public required List<EventDto> Events { get; init; }

        public async Task<PromptDto> NextAsync(Func<PromptDto, bool> match)
        {
            using var cts = new CancellationTokenSource(Timeout);
            while (true)
            {
                var prompt = await Prompts.Reader.ReadAsync(cts.Token);
                if (match(prompt)) return prompt;
            }
        }

        public int PlayerLostEvents => Events.Count(e => e.Type == "PlayerLostEvent");
    }

    private static Harness NewHarness(IPlayerAgent? bobAgent = null)
    {
        var facade = GameFacade.Create("Alice", "Bob", LandDeck("Forest"), LandDeck("Mountain"));
        if (bobAgent != null) facade.ReplaceBobAgent(bobAgent);
        var channel = Channel.CreateUnbounded<PromptDto>();
        facade.SubscribePrompts(p => channel.Writer.TryWrite(p));
        var events = new List<EventDto>();
        facade.Subscribe(e => { lock (events) events.Add(e); });
        return new Harness { Facade = facade, Prompts = channel, Events = events };
    }

    private static async Task KeepBothHandsAsync(Harness h)
    {
        var f = h.Facade;
        var alice = await h.NextAsync(p => p.ExpectedKinds.Contains(nameof(MulliganCommand)));
        await f.SubmitAsync(new MulliganCommand(Keep: true) { PlayerId = alice.PlayerId });
        var bob = await h.NextAsync(p => p.ExpectedKinds.Contains(nameof(MulliganCommand)));
        await f.SubmitAsync(new MulliganCommand(Keep: true) { PlayerId = bob.PlayerId });
    }

    private static async Task<GameDriver.GameResult> AwaitGameAsync(GameFacade facade)
    {
        var game = facade.FullGameTask!;
        (await Task.WhenAny(game, Task.Delay(Timeout))).Should().BeSameAs(game, "the game must end after a concede");
        game.Status.Should().Be(TaskStatus.RanToCompletion, "a concede ends the game; it never faults or cancels it");
        return await game;
    }

    private static RemoteAgent Agent(GameFacade facade, string field) =>
        (RemoteAgent)typeof(GameFacade)
            .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(facade)!;

    [Fact]
    public async Task Concede_WhileOwnPriorityPromptIsPending_EndsTheGameAsALoss()
    {
        var h = NewHarness();
        var f = h.Facade;
        await f.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 10, rng: new GameRandom(7));
        await KeepBothHandsAsync(h);
        await h.NextAsync(p => p.PlayerId == f.Alice.Id && p.ExpectedKinds.Contains(nameof(PassPriorityCommand)));
        Agent(f, "_aliceAgent").HasPending.Should().BeTrue("precondition: Alice's priority prompt is pending");

        await f.ConcedeAsync(f.Alice.Id);

        var result = await AwaitGameAsync(f);
        result.Winner.Should().BeSameAs(f.Bob, "CR 104.2a — the remaining player wins");
        f.Alice.HasLost.Should().BeTrue();
        f.Bob.HasLost.Should().BeFalse();
        h.PlayerLostEvents.Should().Be(1);
        var state = f.GetState();
        state.Players.Single(p => p.Id == f.Alice.Id).HasLost.Should().BeTrue();
        state.Players.Single(p => p.Id == f.Bob.Id).HasLost.Should().BeFalse();
        Agent(f, "_aliceAgent").HasPending.Should().BeFalse("nothing may be left awaiting");
        Agent(f, "_bobAgent").HasPending.Should().BeFalse();
    }

    [Fact]
    public async Task Concede_ByTheSeatWithoutThePrompt_CompletesTheOtherSeatsPendingPrompt()
    {
        // Bob concedes while it is Alice who is being asked (for her mulligan).
        var h = NewHarness();
        var f = h.Facade;
        await f.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 10, rng: new GameRandom(11));
        await h.NextAsync(p => p.PlayerId == f.Alice.Id && p.ExpectedKinds.Contains(nameof(MulliganCommand)));

        await f.ConcedeAsync(f.Bob.Id);

        var result = await AwaitGameAsync(f);
        result.Winner.Should().BeSameAs(f.Alice);
        f.Bob.HasLost.Should().BeTrue();
        h.PlayerLostEvents.Should().Be(1);
        Agent(f, "_aliceAgent").HasPending.Should().BeFalse();
        Agent(f, "_bobAgent").HasPending.Should().BeFalse();
    }

    [Fact]
    public async Task Concede_WhileTheOpponentBotIsActing_EndsTheGameOnceTheBotReturns()
    {
        var bot = new GatedBotAgent();
        var h = NewHarness(bot);
        var f = h.Facade;

        // Bob (bot) plays first; Alice only has to keep her hand.
        await f.StartFullGameAsync(firstPlayerSlot: 1, maxTurns: 10, rng: new GameRandom(3));
        var mulligan = await h.NextAsync(p => p.PlayerId == f.Alice.Id && p.ExpectedKinds.Contains(nameof(MulliganCommand)));
        var keep = f.SubmitAsync(new MulliganCommand(Keep: true) { PlayerId = mulligan.PlayerId });

        (await Task.WhenAny(bot.Deciding, Task.Delay(Timeout))).Should().BeSameAs(bot.Deciding,
            "precondition: the bot is mid-decision");

        var concede = f.ConcedeAsync(f.Alice.Id);
        f.Alice.HasLost.Should().BeTrue("CR 104.3a — the conceding player leaves the game immediately");
        concede.IsCompleted.Should().BeFalse("the engine is still inside the bot's decision");

        bot.Release();
        (await Task.WhenAny(concede, Task.Delay(Timeout))).Should().BeSameAs(concede);
        await concede;
        await keep;

        var result = await AwaitGameAsync(f);
        result.Winner.Should().BeSameAs(f.Bob);
        h.PlayerLostEvents.Should().Be(1);
        f.GetState().Players.Single(p => p.Id == f.Alice.Id).HasLost.Should().BeTrue();
        bot.PriorityDecisions.Should().Be(1, "the engine halts at its game-over gate right after the bot returns");
    }

    [ExpectsContainedFault] // deliberately provokes a contained fault
    [Fact]
    public async Task Concede_WhileAClosureThatIndexesTheAnswerIsWaiting_EndsCleanly()
    {
        // Engine-flow code (here: Bob's seat, standing in for a card closure
        // that asks the opponent to choose) awaits Alice's choice and indexes
        // [0]. The concession declines it with an empty list, so that code
        // throws while the game unwinds: the game must still end as a clean
        // concession, not a faulted FullGameTask.
        GameFacade? facade = null;
        var bot = new GatedBotAgent(async ctx =>
        {
            var alice = Agent(facade!, "_aliceAgent");
            var picks = await alice.ChooseAsync(ctx, new ChoiceRequest(
                ChoiceKind.PickOne, "Choose a player", 1, 1, new object[] { facade!.Alice, facade.Bob }));
            _ = picks[0];
        });
        var h = NewHarness(bot);
        var f = facade = h.Facade;
        await f.StartFullGameAsync(firstPlayerSlot: 1, maxTurns: 10, rng: new GameRandom(3));
        var mulligan = await h.NextAsync(p => p.PlayerId == f.Alice.Id && p.ExpectedKinds.Contains(nameof(MulliganCommand)));
        await f.SubmitAsync(new MulliganCommand(Keep: true) { PlayerId = mulligan.PlayerId });
        await h.NextAsync(p => p.PlayerId == f.Alice.Id && p.ExpectedKinds.Contains(nameof(ChoiceCommand)));

        await f.ConcedeAsync(f.Alice.Id).WaitAsync(Timeout);

        var result = await AwaitGameAsync(f);
        result.Winner.Should().BeSameAs(f.Bob);
        f.Alice.HasLost.Should().BeTrue();
        h.PlayerLostEvents.Should().Be(1);
    }

    [Fact]
    public async Task Concede_Twice_IsANoOp()
    {
        var h = NewHarness();
        var f = h.Facade;
        await f.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 10, rng: new GameRandom(5));
        await h.NextAsync(p => p.ExpectedKinds.Contains(nameof(MulliganCommand)));

        await f.ConcedeAsync(f.Alice.Id);
        await f.ConcedeAsync(f.Alice.Id);
        await f.ConcedeAsync(f.Bob.Id); // the game is already over: also a no-op

        var result = await AwaitGameAsync(f);
        result.Winner.Should().BeSameAs(f.Bob);
        f.Bob.HasLost.Should().BeFalse("a concede after the game ended changes nothing");
        h.PlayerLostEvents.Should().Be(1, "the loss is published exactly once");
    }

    [Fact]
    public async Task Concede_AfterTheGameEndedNormally_IsANoOp()
    {
        var h = NewHarness();
        var f = h.Facade;
        // maxTurns: 0 — the driver ends right after the mulligans.
        await f.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 0, rng: new GameRandom(9));
        await KeepBothHandsAsync(h);
        await AwaitGameAsync(f);

        await f.ConcedeAsync(f.Alice.Id);

        f.Alice.HasLost.Should().BeFalse();
        h.PlayerLostEvents.Should().Be(0);
    }

    [Fact]
    public async Task Concede_UnknownPlayer_ThrowsArgumentException()
    {
        var f = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());

        var act = () => f.ConcedeAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Concede_BeforeTheGameStarts_MarksTheLossAndPublishesIt()
    {
        var h = NewHarness();
        var f = h.Facade;

        await f.ConcedeAsync(f.Bob.Id);

        f.Bob.HasLost.Should().BeTrue();
        h.PlayerLostEvents.Should().Be(1);
    }

    /// <summary>
    /// Stand-in for a bot seat whose first priority decision blocks until the
    /// test releases it — models "the opponent bot is acting" deterministically.
    /// Every other decision is the minimal legal answer.
    /// </summary>
    private sealed class GatedBotAgent(Func<GameContext, Task>? decide = null) : IPlayerAgent
    {
        private readonly TaskCompletionSource _deciding = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _decisions;

        public Task Deciding => _deciding.Task;
        public int PriorityDecisions => _decisions;
        public void Release() => _release.TrySetResult();

        public async Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
        {
            var first = Interlocked.Increment(ref _decisions) == 1;
            _deciding.TrySetResult();
            if (decide != null)
            {
                if (first) await decide(ctx).ConfigureAwait(false);
                return PriorityAction.Pass;
            }
            await _release.Task.ConfigureAwait(false);
            return PriorityAction.Pass;
        }

        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => Task.FromResult(MulliganDecision.Keep);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ICard>>(hand.Take(countToBottom).ToList());
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<object>>(request.LegalCandidates.Take(request.MinTargets).ToList());
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => Task.FromResult(mine);
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => Task.FromResult(ManaPayment.Empty);
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Permanent> eligibleAttackers, CancellationToken ct = default)
            => Task.FromResult(CombatPlan.None);
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Permanent> attackers, IReadOnlyList<Permanent> eligibleBlockers, CancellationToken ct = default)
            => Task.FromResult(BlockPlan.None);
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.ScryAction.ScryDecision(ToBottom: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.SurveilAction.SurveilDecision(ToGraveyard: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
    }
}
