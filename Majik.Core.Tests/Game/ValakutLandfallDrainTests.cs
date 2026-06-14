using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// End-to-end prod-path verification of Valakut, the Molten Pinnacle's landfall
/// trigger (CR 603.1 / 603.3 / 603.4):
///
///   "Whenever a Mountain you control enters, if you control at least five
///    other Mountains, you may have this land deal 3 damage to any target."
///
/// This closes the <c>valakut-may-targeted-landfall</c> deferral by exercising
/// the WHOLE production chain in one real turn:
///   1. Valakut's trigger is BOUND from oracle text by
///      <see cref="OracleTriggeredAbilityBinder"/> (lands' only prod path — never
///      a [CardName] factory), carrying the "any target" <see cref="TargetRequest"/>
///      (gathered live from every player / creature / planeswalker) with
///      <c>MinTargets: 0</c> modelling the "you may" optionality.
///   2. A Mountain entering the controller's battlefield publishes a
///      <see cref="CardMovedEvent"/> on the live bus, which fires the trigger
///      (intervening-if ≥5 OTHER Mountains gates it — CR 603.4).
///   3. The live <see cref="TurnDriver"/> priority loop drains that pending
///      trigger on the AGENT-AWARE async path
///      (<see cref="TriggerManager.PutPendingTriggersOnStackAsync"/>), PROMPTING
///      the controller's agent for the "any target" choice (the gap the
///      deferral named) before the ability resolves.
///
/// Two cases prove both halves of the deferral:
///   - the agent CHOOSES a target → 3 damage lands on exactly that target, and
///   - the agent DECLINES (returns no target, the "you may" no) → clean no-op.
/// </summary>
public class ValakutLandfallDrainTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly StateBasedActions _sba;
    private readonly PriorityManager _priority;
    private readonly EmbeddedCardRepository _repo = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ValakutLandfallDrainTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _sba = new StateBasedActions(_bus, _zones, _triggers);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
    }

    [Fact]
    public async Task MountainEnters_PromptsAgentForAnyTarget_AndDeals3ToTheChosenTarget()
    {
        SetUpValakutWithFiveOtherMountains();

        // Agent that, when asked for the "any target", points 3 damage at Bob.
        var aliceAgent = new ValakutPickingAgent(req =>
        {
            // Choose Bob from the gathered "any target" candidate pool.
            var bob = req.LegalCandidates.OfType<Player>()
                .FirstOrDefault(p => ReferenceEquals(p, _bob));
            return bob != null ? new object[] { bob } : Array.Empty<object>();
        });

        SeedLibrary(_alice, 3);
        SeedLibrary(_bob, 3);
        var driver = NewDriver(aliceAgent, new ValakutPickingAgent(_ => Array.Empty<object>()));

        // During Alice's upkeep, a sixth Mountain enters under her control —
        // this fires Valakut (5 OTHER Mountains already present → intervening-if
        // satisfied) and the live priority loop drains it on the async path.
        QueueMountainEntersAtUpkeep();

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        aliceAgent.ChooseTargetsCalls.Should().BeGreaterThan(0,
            because: "Valakut's landfall trigger must prompt the controller for the 'any target' (CR 603.3)");
        aliceAgent.LastCandidates.Should().Contain(c => ReferenceEquals(c, _bob),
            because: "the 'any target' candidate pool is gathered live and includes the opponent (a player is a legal any-target)");
        _bob.LifeTotal.Should().Be(17,
            because: "Valakut deals 3 to the AGENT-CHOSEN target (CR 119)");
        _alice.LifeTotal.Should().Be(20, because: "the controller is unaffected");
    }

    [Fact]
    public async Task MountainEnters_AgentDeclinesTheMay_IsCleanNoOp()
    {
        SetUpValakutWithFiveOtherMountains();

        // Agent declines the optional "you may" by choosing NO target. With
        // MinTargets: 0 the trigger resolves harmlessly (CR 117.5 / 608.2b).
        var aliceAgent = new ValakutPickingAgent(_ => Array.Empty<object>());

        SeedLibrary(_alice, 3);
        SeedLibrary(_bob, 3);
        var driver = NewDriver(aliceAgent, new ValakutPickingAgent(_ => Array.Empty<object>()));

        QueueMountainEntersAtUpkeep();

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        _bob.LifeTotal.Should().Be(20, because: "the controller declined the 'you may' → no damage dealt");
        _alice.LifeTotal.Should().Be(20);
        _stack.IsEmpty.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Setup helpers.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Put a prod-bound Valakut plus exactly five OTHER Mountains under Alice's
    /// control. The Valakut trigger is bound from real oracle text via the
    /// binder chain (its only prod path) and registered with the live
    /// TriggerManager so the bus drives it.
    /// </summary>
    private void SetUpValakutWithFiveOtherMountains()
    {
        var entity = _repo.GetByName("Valakut, the Molten Pinnacle")!;
        var parsed = TypeLineParser.Parse(entity.TypeLine);
        var valakut = new Land("Valakut, the Molten Pinnacle", parsed.Supertypes, parsed.Subtypes);
        valakut.SetOwner(_alice);
        valakut.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(valakut);
        valakut.SetZone(ZoneType.Battlefield);

        foreach (var trig in OracleTriggeredAbilityBinder.Bind(
                     valakut, entity, _alice, new[] { _alice, _bob }))
        {
            _triggers.RegisterTriggeredAbility(trig);
        }

        for (var i = 0; i < 5; i++)
        {
            var mtn = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain })
            { Owner = _alice, Controller = _alice };
            _alice.Zones.Battlefield.AddCard(mtn);
            mtn.SetZone(ZoneType.Battlefield);
        }
    }

    /// <summary>
    /// At Alice's upkeep, move a fresh Mountain from her library into play via
    /// <see cref="ZoneService.MoveCard"/> so the resulting
    /// <see cref="CardMovedEvent"/> fires Valakut's landfall trigger inside the
    /// live turn (so the priority loop, not a manual call, drains it).
    /// </summary>
    private void QueueMountainEntersAtUpkeep()
    {
        var entering = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain })
        { Owner = _alice, Controller = _alice };
        _alice.Zones.Library.AddCard(entering);
        entering.SetZone(ZoneType.Library);

        var fired = false;
        _bus.Subscribe<StepStartedEvent>(e =>
        {
            if (fired) return;
            if (e.StepType != StepStateType.Upkeep || !ReferenceEquals(e.Player, _alice)) return;
            fired = true;
            // Library → battlefield publishes CardMovedEvent(ToZone=Battlefield);
            // Valakut's bound trigger matches it (Mountain, controller=Alice,
            // ≥5 other Mountains).
            _zones.MoveCard(entering, ZoneType.Library, ZoneType.Battlefield);
        });
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

    /// <summary>
    /// Passes every priority window; answers ChooseTargetsAsync via the supplied
    /// selector, recording how many times it was asked and the last candidate
    /// pool it saw (to assert the live "any target" gather).
    /// </summary>
    private sealed class ValakutPickingAgent : IPlayerAgent
    {
        private readonly Func<TargetRequest, IReadOnlyList<object>> _pick;
        public int ChooseTargetsCalls { get; private set; }
        public IReadOnlyList<object> LastCandidates { get; private set; } = Array.Empty<object>();

        public ValakutPickingAgent(Func<TargetRequest, IReadOnlyList<object>> pick) => _pick = pick;

        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
        {
            ChooseTargetsCalls++;
            LastCandidates = request.LegalCandidates;
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
