using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Effects;
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
/// CR 704.1 / 704.3 / 704.4 — state-based actions must be checked every time a
/// player WOULD receive priority (and looped until none apply), not only at
/// turn boundaries. A creature that drops to 0 toughness OUTSIDE combat — a
/// 0/0 entering the battlefield, a creature reduced to 0 by an instant — must
/// be put into the graveyard IMMEDIATELY (before its controller regains
/// priority), not linger until the next turn.
///
/// These run a real turn through <see cref="TurnDriver"/> (the production
/// PriorityRound → PriorityLoop path) so the SBA-before-priority fix manifests
/// through the live loop, and assert the dying creature is gone by the actor's
/// NEXT priority window.
/// </summary>
public class SbaBeforePriorityTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly StateBasedActions _sba;
    private readonly PriorityManager _priority;
    private readonly ContinuousEffectsService _effects;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SbaBeforePriorityTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _sba = new StateBasedActions(_bus, _zones, _triggers);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
        _effects = new ContinuousEffectsService(_bus);
    }

    [Fact]
    public async Task ZeroToughnessCreatureEntering_DiesBeforeControllerRegainsPriority()
    {
        // Alice controls a "Factory" permanent with a no-cost activated ability
        // whose resolution puts a fresh 0/0 creature onto her battlefield.
        // After the ability resolves the engine grants priority again — at that
        // point CR 704.1 SBAs must already have moved the 0/0 to the graveyard.
        var factory = new Artifact("Factory", "0");
        factory.SetOwner(_alice);
        factory.SetController(_alice);
        factory.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(factory);

        Creature? spawned = null;
        var makeToken = new Effect(
            "create a 0/0 creature",
            () =>
            {
                spawned = new Creature("Zero Zero", "", 0, 0);
                spawned.SetOwner(_alice);
                spawned.SetController(_alice);
                // Place the 0/0 on Alice's battlefield (the zone the SBA layer
                // scans). It is a 0/0 with no counters → CR 704.5f applies.
                _alice.Zones.Battlefield.AddCard(spawned);
                spawned.SetZone(ZoneType.Battlefield);
            });
        var ability = new ActivatedAbility(
            source: factory,
            controller: _alice,
            costs: null,
            effects: new IEffect[] { makeToken });
        factory.AddAbility(ability);

        // Agent: first priority window → activate the spawn ability; on the
        // very next window record whether the 0/0 has already died, then pass
        // forever. The recorded snapshot is taken the next time Alice receives
        // priority after the ability resolved.
        bool? aliveAtNextPriority = null;
        var activated = false;
        var agent = new ScriptedSpawnAgent(
            onPriority: () =>
            {
                if (!activated)
                {
                    activated = true;
                    return new PriorityAction.ActivateAbility(ability, Array.Empty<object>());
                }
                // Once the ability has resolved (spawned != null), the very
                // next time Alice is offered priority record whether the 0/0
                // is still on the battlefield.
                if (spawned != null)
                {
                    aliveAtNextPriority ??= spawned.Zone == ZoneType.Battlefield;
                }
                return PriorityAction.Pass;
            });

        SeedLibrary(_alice, 3);
        SeedLibrary(_bob, 3);
        var driver = NewDriver(agent, new PassAgent());

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        aliveAtNextPriority.Should().BeFalse(
            because: "CR 704.1 — the 0/0 must be moved to the graveyard by the SBA check " +
                     "that runs BEFORE Alice regains priority, not left on the battlefield");
        spawned!.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(spawned);
    }

    [Fact]
    public async Task NonCombatLethalToughnessReduction_KillsCreatureBeforePriority()
    {
        // A 1/1 Alice controls. Her activated ability puts a -1/-1 counter on
        // it, dropping it to 0/0. The SBA-before-priority check must kill it
        // before she regains priority (CR 704.5f, non-combat).
        var bear = new Creature("One One", "", 1, 1) { ActiveEffects = _effects };
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var shrinker = new Artifact("Shrinker", "0");
        shrinker.SetOwner(_alice);
        shrinker.SetController(_alice);
        shrinker.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(shrinker);

        var resolved = false;
        var minus = new Effect(
            "put a -1/-1 counter on the 1/1",
            () =>
            {
                bear.Counters.Add(CounterType.MinusOneMinusOne, 1);
                resolved = true;
            });
        var ability = new ActivatedAbility(
            source: shrinker,
            controller: _alice,
            costs: null,
            effects: new IEffect[] { minus });
        shrinker.AddAbility(ability);

        bool? aliveAtNextPriority = null;
        var fired = false;
        var agent = new ScriptedSpawnAgent(
            onPriority: () =>
            {
                if (!fired)
                {
                    fired = true;
                    return new PriorityAction.ActivateAbility(ability, Array.Empty<object>());
                }
                // Only sample once the ability has RESOLVED (counter applied);
                // before that the bear is legitimately still a 1/1.
                if (resolved)
                {
                    aliveAtNextPriority ??= bear.Zone == ZoneType.Battlefield;
                }
                return PriorityAction.Pass;
            });

        SeedLibrary(_alice, 3);
        SeedLibrary(_bob, 3);
        var driver = NewDriver(agent, new PassAgent());

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        aliveAtNextPriority.Should().BeFalse(
            because: "a 1/1 reduced to 0/0 by a -1/-1 counter must die via the CR 704.1 " +
                     "SBA check before its controller regains priority");
        bear.Zone.Should().Be(ZoneType.Graveyard);
    }

    private TurnDriver NewDriver(IPlayerAgent aliceAgent, IPlayerAgent bobAgent) =>
        new TurnDriver(
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
            eventBus: _bus);

    private static void SeedLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = NamedCardFactory.Create("Mountain", p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    /// <summary>Drives priority via the supplied selector; passes everything else.</summary>
    private sealed class ScriptedSpawnAgent : IPlayerAgent
    {
        private readonly Func<PriorityAction> _onPriority;
        public ScriptedSpawnAgent(Func<PriorityAction> onPriority) => _onPriority = onPriority;

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => Task.FromResult(_onPriority());

        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => Task.FromResult(MulliganDecision.Keep);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ICard>>(Array.Empty<ICard>());
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<Majik.Core.Cards.BotIntent>? modeIntents = null, CancellationToken ct = default) => Task.FromResult(0);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ITriggeredAbility>>(mine);
        public Task<Majik.Core.Players.Agents.ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => Task.FromResult(Majik.Core.Players.Agents.ManaPayment.Empty);
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
            => Task.FromResult(CombatPlan.None);
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => Task.FromResult(BlockPlan.None);
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.ScryAction.ScryDecision(ToBottom: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.SurveilAction.SurveilDecision(ToGraveyard: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
    }

    private sealed class PassAgent : IPlayerAgent
    {
        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => Task.FromResult<PriorityAction>(PriorityAction.Pass);
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => Task.FromResult(MulliganDecision.Keep);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ICard>>(Array.Empty<ICard>());
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<Majik.Core.Cards.BotIntent>? modeIntents = null, CancellationToken ct = default) => Task.FromResult(0);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ITriggeredAbility>>(mine);
        public Task<Majik.Core.Players.Agents.ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => Task.FromResult(Majik.Core.Players.Agents.ManaPayment.Empty);
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
            => Task.FromResult(CombatPlan.None);
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => Task.FromResult(BlockPlan.None);
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.ScryAction.ScryDecision(ToBottom: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.SurveilAction.SurveilDecision(ToGraveyard: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
    }
}
