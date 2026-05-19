using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

public class TurnDriverTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly StateBasedActions _sba;
    private readonly PriorityManager _priority;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public TurnDriverTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _sba = new StateBasedActions(_bus, _zones, _triggers);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
    }

    [Fact]
    public async Task RunTurnAsync_BothBots_CompletesWithoutException()
    {
        SeedLibrary(_alice, 5);
        SeedLibrary(_bob, 5);
        var driver = NewDriver();

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        _stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task RunTurnAsync_DrawStep_DrawsACard_OnNonFirstTurn()
    {
        SeedLibrary(_alice, 3);
        var driver = NewDriver();

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        _alice.Zones.Hand.Count.Should().Be(1);
        _alice.Zones.Library.Count.Should().Be(2);
    }

    [Fact]
    public async Task RunTurnAsync_FirstTurn_SkipsDrawStep()
    {
        SeedLibrary(_alice, 3);
        var driver = NewDriver();

        await driver.RunTurnAsync(_alice, turnNumber: 1);

        _alice.Zones.Hand.Count.Should().Be(0);
        _alice.Zones.Library.Count.Should().Be(3);
    }

    [Fact]
    public async Task RunTurnAsync_UntapStep_UntapsControllerPermanents()
    {
        var mountain = NamedCardFactory.Create("Mountain", _alice);
        mountain.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(mountain);
        ((Permanent)mountain).Tap();
        ((Permanent)mountain).IsTapped.Should().BeTrue();
        SeedLibrary(_alice, 3);
        var driver = NewDriver();

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        ((Permanent)mountain).IsTapped.Should().BeFalse();
    }

    [Fact]
    public async Task RunTurnAsync_AttackerWithoutBlocker_DamagesDefender()
    {
        var bear = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);
        bear.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(bear);
        bear.HasSummoningSickness = false;
        SeedLibrary(_alice, 3);
        SeedLibrary(_bob, 3);

        var aliceAgent = new SequencedAgent();
        // priority rounds in each phase — agent passes each time
        // attackers: declare bear
        aliceAgent.AttackPlan = new CombatPlan(new[]
        {
            new Majik.Core.Players.Agents.AttackerDeclaration(bear, _bob),
        });
        var bobAgent = new SequencedAgent();
        bobAgent.BlockPlan = BlockPlan.None;

        var driver = NewDriver(aliceAgent, bobAgent);
        await driver.RunTurnAsync(_alice, turnNumber: 2);

        _bob.LifeTotal.Should().Be(18);
        bear.IsTapped.Should().BeTrue();
    }

    [Fact]
    public async Task RunTurnAsync_Cleanup_DiscardsDownTo7()
    {
        for (var i = 0; i < 10; i++)
        {
            var card = NamedCardFactory.Create($"Mountain", _alice);
            _alice.Zones.Hand.AddCard(card);
            card.Zone = ZoneType.Hand;
        }
        SeedLibrary(_alice, 3);
        var driver = NewDriver();

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        _alice.Zones.Hand.Count.Should().Be(7);
    }

    private TurnDriver NewDriver(IPlayerAgent? aliceAgent = null, IPlayerAgent? bobAgent = null)
    {
        aliceAgent ??= new DeterministicBotAgent();
        bobAgent ??= new DeterministicBotAgent();
        return new TurnDriver(
            players: new[] { _alice, _bob },
            agents: new Dictionary<Player, IPlayerAgent>
            {
                [_alice] = aliceAgent,
                [_bob] = bobAgent,
            },
            stack: _stack,
            zoneService: _zones,
            triggerManager: _triggers,
            stackResolver: _resolver,
            stateBasedActions: _sba,
            priorityManager: _priority,
            combatFlow: new CombatFlow(_bus, _sba));
    }

    private static void SeedLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = NamedCardFactory.Create("Mountain", p);
            p.Zones.Library.AddCard(c);
            c.Zone = ZoneType.Library;
        }
    }

    /// <summary>
    /// Bot that always passes priority but supplies pre-set combat plans.
    /// </summary>
    private sealed class SequencedAgent : IPlayerAgent
    {
        public CombatPlan AttackPlan { get; set; } = CombatPlan.None;
        public BlockPlan BlockPlan { get; set; } = BlockPlan.None;

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => Task.FromResult(PriorityAction.Pass);
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => Task.FromResult(MulliganDecision.Keep);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ICard>>(Array.Empty<ICard>());
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, CancellationToken ct = default) => Task.FromResult(0);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ITriggeredAbility>>(mine);
        public Task<Majik.Core.Players.Agents.ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => Task.FromResult(Majik.Core.Players.Agents.ManaPayment.Empty);
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
            => Task.FromResult(AttackPlan);
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => Task.FromResult(BlockPlan);
    }
}
