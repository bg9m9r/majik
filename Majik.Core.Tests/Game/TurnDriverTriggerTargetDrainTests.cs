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
/// CR 603.3 — the live priority loop must drain pending TRIGGERED abilities
/// onto the stack on the agent-aware ASYNC path, so a targeted trigger prompts
/// its controller's agent for targets before resolving. Before this fix the
/// loop drained via PriorityManager's SYNCHRONOUS
/// <see cref="TriggerManager.PutPendingTriggersOnStack"/> (no agent, no
/// targets) and every bound targeted trigger silently auto-picked
/// first-eligible in real games.
///
/// These tests run a real turn through <see cref="TurnDriver"/> (the production
/// PriorityRound → PriorityLoop path) and assert:
///   1. a targeted trigger prompts the controller's agent and the SPECIFIC
///      chosen legal target (not first-eligible) is affected, and
///   2. a non-targeted trigger still fires correctly (regression).
/// </summary>
public class TurnDriverTriggerTargetDrainTests
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

    public TurnDriverTriggerTargetDrainTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _sba = new StateBasedActions(_bus, _zones, _triggers);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
    }

    [Fact]
    public async Task TargetedUpkeepTrigger_PromptsController_AndHitsTheChosenTarget()
    {
        // Two legal targets Bob controls. The trigger picks among them; the
        // agent must be PROMPTED and its choice honoured — not auto-picked
        // first-eligible (which would always hit `first`).
        var first = new Creature("First Target", "G", 0, 9)
        {
            Owner = _bob, Zone = ZoneType.Battlefield,
        };
        var second = new Creature("Second Target", "G", 0, 9)
        {
            Owner = _bob, Zone = ZoneType.Battlefield,
        };
        _bob.Zones.Battlefield.AddCard(first);
        _bob.Zones.Battlefield.AddCard(second);

        // Alice controls a permanent whose upkeep trigger deals 1 damage to a
        // chosen "target creature an opponent controls" (1..1 TargetRequest
        // over both of Bob's creatures). Shape mirrors a binder-bound targeted
        // trigger (e.g. Leyline of Lightning's "deal 1 to target …").
        var source = new Enchantment("Pinger", "R", supertypes: null, subtypes: null);
        source.SetOwner(_alice);
        source.SetController(_alice);
        source.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(source);

        // The effect records WHICH creature the resolving trigger targeted, in
        // a list that survives end-of-turn cleanup (marked damage would be
        // wiped at the cleanup step, so we capture the chosen target's identity
        // directly — the point is to prove the AGENT's pick was honoured, not
        // first-eligible).
        var hitTargets = new List<Creature>();
        TriggeredAbility? trigger = null;
        var dealDamage = new Effect(
            "deal 1 damage to chosen target",
            () =>
            {
                if (trigger != null
                    && trigger.ChosenTargets.Count > 0
                    && trigger.ChosenTargets[0].Count > 0
                    && trigger.ChosenTargets[0][0] is Creature c)
                {
                    c.TakeDamage(1);
                    hitTargets.Add(c);
                }
            });

        trigger = new TriggeredAbility(
            source: source,
            controller: _alice,
            condition: new EventTriggerCondition<StepStartedEvent>((e, _) =>
                e.StepType == StepStateType.Upkeep && ReferenceEquals(e.Player, _alice)),
            effects: new IEffect[] { dealDamage },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: new object[] { first, second }),
            });
        _triggers.RegisterTriggeredAbility(trigger);

        // Agent that ALWAYS picks the SECOND candidate. If targets were not
        // collected from the agent (the old sync drain), the effect would
        // auto-pick first-eligible (`first`) instead.
        var aliceAgent = new TargetPickingAgent(req => new object[] { req.LegalCandidates[1] });
        var bobAgent = new TargetPickingAgent(_ => Array.Empty<object>());

        SeedLibrary(_alice, 3);
        SeedLibrary(_bob, 3);
        var driver = NewDriver(aliceAgent, bobAgent);

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        aliceAgent.ChooseTargetsCalls.Should().BeGreaterThan(0,
            because: "the targeted trigger must prompt the controller's agent for targets (CR 603.3)");
        hitTargets.Should().ContainSingle(because: "the targeted trigger resolves once this turn")
            .Which.Should().BeSameAs(second,
                because: "the agent chose the SECOND creature; the old target-less sync drain would have auto-picked first-eligible (first)");
    }

    [Fact]
    public async Task NonTargetedUpkeepTrigger_StillFires_Regression()
    {
        // A non-targeted upkeep trigger (no TargetRequests) must still drain
        // and resolve exactly as before — the async drain path skips straight
        // to the push for triggers with no requests.
        var source = new Enchantment("Pinger", "R", supertypes: null, subtypes: null);
        source.SetOwner(_alice);
        source.SetController(_alice);
        source.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(source);

        var gainLife = new Effect("gain 3 life", () => _alice.GainLife(3));
        var trigger = new TriggeredAbility(
            source: source,
            controller: _alice,
            condition: new EventTriggerCondition<StepStartedEvent>((e, _) =>
                e.StepType == StepStateType.Upkeep && ReferenceEquals(e.Player, _alice)),
            effects: new IEffect[] { gainLife },
            activeZones: new[] { ZoneType.Battlefield });
        _triggers.RegisterTriggeredAbility(trigger);

        SeedLibrary(_alice, 3);
        SeedLibrary(_bob, 3);
        var driver = NewDriver();

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        _alice.LifeTotal.Should().Be(23, because: "the non-targeted upkeep trigger resolves once");
        _stack.IsEmpty.Should().BeTrue();
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
            combatFlow: new CombatFlow(_bus, _sba),
            // The TriggerManager + StackResolver subscribe to this bus; wire it
            // so SetPhase publishes StepStartedEvent and the upkeep trigger
            // actually fires (and its drain runs on the live PriorityLoop path).
            eventBus: _bus);
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

    /// <summary>
    /// Passes every priority window but answers ChooseTargetsAsync via the
    /// supplied selector, recording how many times it was asked for targets.
    /// </summary>
    private sealed class TargetPickingAgent : IPlayerAgent
    {
        private readonly Func<TargetRequest, IReadOnlyList<object>> _pick;
        public int ChooseTargetsCalls { get; private set; }

        public TargetPickingAgent(Func<TargetRequest, IReadOnlyList<object>> pick) => _pick = pick;

        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
        {
            ChooseTargetsCalls++;
            return Task.FromResult(_pick(request));
        }

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => Task.FromResult(PriorityAction.Pass);
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
