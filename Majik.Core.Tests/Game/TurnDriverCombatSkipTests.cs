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

/// <summary>
/// Combat fast-path coverage — when the active player has zero eligible
/// attackers, TurnDriver MUST NOT call DeclareAttackersAsync. Mirrors the
/// "skip combat" UX shipped by MTG Arena / MTGO: there is nothing to
/// declare, so don't pop a modal. Honours the FullControl override
/// (everything prompts when FC is on).
/// </summary>
public class TurnDriverCombatSkipTests
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

    public TurnDriverCombatSkipTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _sba = new StateBasedActions(_bus, _zones, _triggers);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
    }

    private TurnDriver NewDriver(
        CountingAgent? aliceAgent = null,
        IPlayerAgent? bobAgent = null,
        System.Func<Player, Majik.Core.Game.IAutoPassPrefsView?>? prefsProvider = null)
    {
        aliceAgent ??= new CountingAgent();
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
            combatFlow: new CombatFlow(_bus, _sba),
            eventBus: _bus,
            autoPassPrefsProvider: prefsProvider);
    }

    private static void SeedLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = NamedCardFactory.Create("Mountain", p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    [Fact]
    public async Task NoEligibleAttackers_DefaultPrefs_DeclareAttackersAsyncNotCalled()
    {
        // Alice's only creature is tapped and will get re-tapped on the
        // pre-combat-main step transition (mid-turn) so it can't attack
        // even after untap. Zero eligible attackers → the engine MUST
        // skip the DeclareAttackers prompt.
        var bear = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        // Re-tap the bear when we hit the precombat main, so it survives
        // the upstream UntapStep with IsTapped == true. This simulates
        // (in a test-friendly way) a creature that was tapped by an
        // opponent's effect mid-turn.
        _bus.Subscribe<Majik.Core.Events.StepStartedEvent>(e =>
        {
            if (e.StepType == PhaseStateType.PreCombatMain && !bear.IsTapped)
            {
                bear.Tap();
            }
        });
        SeedLibrary(_alice, 3);
        SeedLibrary(_bob, 3);

        var alice = new CountingAgent();
        var driver = NewDriver(alice);

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        alice.DeclareAttackersCalls.Should().Be(0,
            "a tapped creature is not a legal attacker (CR 508.1c), so the prompt MUST be skipped");
    }

    [Fact]
    public async Task EmptyBoard_DefaultPrefs_DeclareAttackersAsyncNotCalled()
    {
        // No creatures at all → trivially no eligible attackers → skip.
        SeedLibrary(_alice, 3);
        SeedLibrary(_bob, 3);

        var alice = new CountingAgent();
        var driver = NewDriver(alice);

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        alice.DeclareAttackersCalls.Should().Be(0);
    }

    [Fact]
    public async Task UnsickCreature_StillPromptsDeclareAttackers()
    {
        // Aggressive creature with no sickness — must still prompt; the
        // human (or bot) may have legitimate "don't attack" reasons (e.g.
        // hold back to block) but the choice is theirs.
        var bear = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.HasSummoningSickness = false;
        SeedLibrary(_alice, 3);
        SeedLibrary(_bob, 3);

        var alice = new CountingAgent();
        var driver = NewDriver(alice);

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        alice.DeclareAttackersCalls.Should().Be(1);
    }

    [Fact]
    public async Task FullControl_ON_StillPromptsEvenWithNoAttackers()
    {
        // FullControl ON is the "give me every prompt" toggle. Even with
        // no eligible attackers (empty board), MUST prompt. Empty board
        // keeps the test setup simple — the only requirement is that the
        // active player has zero eligible attackers when combat begins.
        SeedLibrary(_alice, 3);
        SeedLibrary(_bob, 3);

        var alice = new CountingAgent();
        var driver = NewDriver(alice,
            prefsProvider: _ => new FullControlPrefs());

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        alice.DeclareAttackersCalls.Should().Be(1,
            "FullControl overrides the auto-skip");
    }

    private sealed class FullControlPrefs : Majik.Core.Game.IAutoPassPrefsView
    {
        public bool FullControl => true;
        public IReadOnlyDictionary<string, string> PhaseStops { get; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Agent that delegates to DeterministicBotAgent for every prompt
    /// except DeclareAttackersAsync, which it counts. The count drives
    /// each test's assertion.
    /// </summary>
    private sealed class CountingAgent : IPlayerAgent
    {
        private readonly DeterministicBotAgent _inner = new();
        public int DeclareAttackersCalls { get; private set; }

        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
        {
            DeclareAttackersCalls++;
            return _inner.DeclareAttackersAsync(ctx, eligibleAttackers, ct);
        }

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => _inner.ChoosePriorityActionAsync(ctx, ct);
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => _inner.ChooseMulliganAsync(ctx, hand, mulligansTaken, ct);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => _inner.ChooseCardsToBottomAsync(ctx, hand, countToBottom, ct);
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => _inner.ChooseTargetsAsync(ctx, request, ct);
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
            => _inner.ChooseXAsync(ctx, source, ct);
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<Majik.Core.Cards.BotIntent>? modeIntents = null, CancellationToken ct = default)
            => _inner.ChooseModeAsync(ctx, modes, modeIntents, ct);
        public Task<IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility> mine, CancellationToken ct = default)
            => _inner.OrderTriggersAsync(ctx, mine, ct);
        public Task<Majik.Core.Players.Agents.ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => _inner.ChooseManaSourcesAsync(ctx, cost, ct);
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => _inner.DeclareBlockersAsync(ctx, attackers, eligibleBlockers, ct);
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => _inner.ChooseScryDecisionAsync(ctx, peeked, ct);
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => _inner.ChooseSurveilDecisionAsync(ctx, peeked, ct);
    }
}
