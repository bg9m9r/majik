using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 601.2c / CR 732.1 — a cast that fails AFTER the mana-payment step must
/// not waste the mana. CR 601.2 puts target selection (601.2c) BEFORE cost
/// payment (601.2h); if the cast becomes illegal at target collection (no
/// legal targets for a required target), CR 732.1 reverses the whole action:
/// "the entire action is reversed and any payments already made are
/// canceled... the spell returns to the zone it came from", and the player
/// "may also reverse any legal mana abilities" — i.e. lands tapped for the
/// cast untap and the pool returns to its pre-payment state.
///
/// Production bug (live bot): TurnDriver.DispatchCast paid mana BEFORE
/// SpellCastFlow.CastAsync's target collection, and the failure path only
/// rotated the hand — the bot tapped its lands, the cast failed at
/// targeting, and the mana evaporated at step end. Repeatedly observed as
/// "bot taps lands and casts nothing".
/// </summary>
public class TurnDriverCastManaRefundTests
{
    /// <summary>
    /// Failure injection: a {R} instant whose SpellDefinition requires one
    /// target but has NO legal candidates. The agent pays with a Mountain;
    /// target collection then throws (CR 601.2c). The Mountain must remain
    /// untapped and the card in hand (CR 732.1 rewind).
    /// </summary>
    [Fact]
    public async Task FailedTargeting_DoesNotWasteTappedLand_CR732Rewind()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var replacements = new ReplacementBus();
        var zones = new ZoneService(bus, replacements);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var players = new List<Player> { alice, bob };
        var priority = new PriorityManager(players, stack, bus, triggers);

        // Alice's only mana source — the land the failed cast must not waste.
        var mountain = (Permanent)NamedCardFactory.Create("Mountain", alice);
        mountain.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(mountain);

        var bolt = new Instant("Doom Bolt", "{R}") { Owner = alice, Zone = ZoneType.Hand };
        alice.Zones.Hand.AddCard(bolt);

        foreach (var p in players)
        {
            for (var i = 0; i < 5; i++)
            {
                var c = NamedCardFactory.Create("Island", p);
                c.SetZone(ZoneType.Library);
                p.Zones.Library.AddCard(c);
            }
        }

        // SpellDefinition with an unfulfillable required target: min 1,
        // zero legal candidates → TargetCollection.CollectAsync throws
        // (throwOnInsufficient, CR 601.2c) AFTER TurnDriver paid the mana.
        Func<ICard, Player, Majik.Core.Stack.Stack?, SpellDefinition?> defResolver =
            (card, caster, stk) => card.Name == "Doom Bolt"
                ? new SpellDefinition(
                    Modes: Array.Empty<string>(),
                    HasVariableX: false,
                    TargetRequests: new[]
                    {
                        new TargetRequest(
                            "target creature", MinTargets: 1, MaxTargets: 1,
                            LegalCandidates: Array.Empty<object>()),
                    },
                    EffectFactory: _ => Array.Empty<IEffect>())
                : null;

        var inner = new ScriptedAgent();
        inner.QueueMana(new ManaPayment(new[] { (ICard)mountain }));
        inner.QueueTargets(Array.Empty<object>()); // no legal target to pick
        var aliceAgent = new MainPhaseCastAgent(inner, bolt, alice);

        var bobAgent = new ScriptedAgent();
        for (var i = 0; i < 30; i++) bobAgent.QueuePriority(PriorityAction.Pass);

        var driver = new TurnDriver(
            players,
            new Dictionary<Player, IPlayerAgent> { [alice] = aliceAgent, [bob] = bobAgent },
            stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            continuousEffects: null,
            spellDefinitionResolver: defResolver,
            replacements: replacements,
            landDropTracker: new LandDropTracker(),
            eventBus: bus);

        await driver.RunTurnAsync(alice, turnNumber: 2);

        // CR 732.1 — the illegal cast rewinds completely.
        alice.Zones.Hand.GetCards().Should().Contain(bolt,
            "the spell returns to the zone it came from (CR 732.1)");
        stack.Count.Should().Be(0);
        mountain.IsTapped.Should().BeFalse(
            "mana abilities activated for an illegal cast are reversed " +
            "(CR 732.1) — the land must not stay tapped for nothing");
    }

    /// <summary>
    /// Same failure injection, but the cost is covered by FLOATING mana
    /// (auto-pay-from-pool path: no source prompt at all). The pre-payment
    /// pool must be restored when the cast fails at target collection.
    /// </summary>
    [Fact]
    public async Task FailedTargeting_RestoresFloatingManaPool_CR732Rewind()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var replacements = new ReplacementBus();
        var zones = new ZoneService(bus, replacements);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var players = new List<Player> { alice, bob };
        var priority = new PriorityManager(players, stack, bus, triggers);

        var bolt = new Instant("Doom Bolt", "{R}") { Owner = alice, Zone = ZoneType.Hand };
        alice.Zones.Hand.AddCard(bolt);

        foreach (var p in players)
        {
            for (var i = 0; i < 5; i++)
            {
                var c = NamedCardFactory.Create("Island", p);
                c.SetZone(ZoneType.Library);
                p.Zones.Library.AddCard(c);
            }
        }

        Func<ICard, Player, Majik.Core.Stack.Stack?, SpellDefinition?> defResolver =
            (card, caster, stk) => card.Name == "Doom Bolt"
                ? new SpellDefinition(
                    Modes: Array.Empty<string>(),
                    HasVariableX: false,
                    TargetRequests: new[]
                    {
                        new TargetRequest(
                            "target creature", MinTargets: 1, MaxTargets: 1,
                            LegalCandidates: Array.Empty<object>()),
                    },
                    EffectFactory: _ => Array.Empty<IEffect>())
                : null;

        var inner = new ScriptedAgent();
        inner.QueueTargets(Array.Empty<object>()); // no legal target to pick
        var aliceAgent = new PoolFloatingMainPhaseCastAgent(inner, bolt, alice);

        var bobAgent = new ScriptedAgent();
        for (var i = 0; i < 30; i++) bobAgent.QueuePriority(PriorityAction.Pass);

        var driver = new TurnDriver(
            players,
            new Dictionary<Player, IPlayerAgent> { [alice] = aliceAgent, [bob] = bobAgent },
            stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            continuousEffects: null,
            spellDefinitionResolver: defResolver,
            replacements: replacements,
            landDropTracker: new LandDropTracker(),
            eventBus: bus);

        await driver.RunTurnAsync(alice, turnNumber: 2);

        alice.Zones.Hand.GetCards().Should().Contain(bolt,
            "the spell returns to the zone it came from (CR 732.1)");
        stack.Count.Should().Be(0);
        // Pools are swept at cleanup, so assert the snapshot the agent took
        // at its first priority window AFTER the failed cast (same turn).
        aliceAgent.PoolAfterCastAttempt.Should().NotBeNull();
        aliceAgent.PoolAfterCastAttempt!.Red.Should().Be(1,
            "payments already made are canceled (CR 732.1) — the floating " +
            "{R} stays in the pool when the cast fails at targeting");
    }

    /// <summary>
    /// CR 601.2c / 601.2h ordering, pinned at the SpellCastFlow level: the
    /// payment callback must run AFTER target collection, so a targeting
    /// failure never reaches the payment.
    /// </summary>
    [Fact]
    public async Task CastAsync_PaymentCallback_RunsAfterTargetCollection()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bolt = new Instant("Bolt", "{R}") { Owner = alice, Zone = ZoneType.Hand };
        alice.Zones.Hand.AddCard(bolt);

        var events = new List<string>();
        var agent = new TargetRecordingAgent(events, new object[] { bob });
        var ctx = new GameContext(
            alice, new[] { alice, bob }, alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("any target", 1, 1, new object[] { bob }),
            },
            EffectFactory: _ => Array.Empty<IEffect>());

        await flow.CastAsync(
            alice, bolt, def, agent, ctx,
            preChosenMana: ManaPayment.Empty,
            payManaCost: _ => { events.Add("pay"); return true; });

        events.Should().Equal(new[] { "targets", "pay" },
            "CR 601.2c (targets) precedes CR 601.2h (payment)");
        stack.Count.Should().Be(1);
    }

    /// <summary>
    /// CR 601.2c / CR 732.1 — when target collection throws, the payment
    /// callback must never run and the card stays in hand.
    /// </summary>
    [Fact]
    public async Task CastAsync_TargetingFails_PaymentCallbackNeverRuns()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);
        var alice = new Player("Alice", 20);
        var bolt = new Instant("Bolt", "{R}") { Owner = alice, Zone = ZoneType.Hand };
        alice.Zones.Hand.AddCard(bolt);

        var paid = false;
        var agent = new TargetRecordingAgent(new List<string>(), Array.Empty<object>());
        var ctx = new GameContext(
            alice, new[] { alice }, alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: _ => Array.Empty<IEffect>());

        var act = async () => await flow.CastAsync(
            alice, bolt, def, agent, ctx,
            preChosenMana: ManaPayment.Empty,
            payManaCost: _ => { paid = true; return true; });

        await act.Should().ThrowAsync<InvalidOperationException>();
        paid.Should().BeFalse(
            "an illegal cast must abort before the payment step (CR 732.1)");
        bolt.Zone.Should().Be(ZoneType.Hand);
        stack.Count.Should().Be(0);
    }

    /// <summary>
    /// CR 601.2h — a payment callback returning false makes the cast illegal:
    /// CastAsync throws and the card never reaches the stack.
    /// </summary>
    [Fact]
    public async Task CastAsync_PaymentFails_CastIsIllegal_CardStaysInHand()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);
        var alice = new Player("Alice", 20);
        var bolt = new Instant("Bolt", "{R}") { Owner = alice, Zone = ZoneType.Hand };
        alice.Zones.Hand.AddCard(bolt);

        var agent = new TargetRecordingAgent(new List<string>(), Array.Empty<object>());
        var ctx = new GameContext(
            alice, new[] { alice }, alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => Array.Empty<IEffect>());

        var act = async () => await flow.CastAsync(
            alice, bolt, def, agent, ctx,
            preChosenMana: ManaPayment.Empty,
            payManaCost: _ => false);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mana payment failed*");
        bolt.Zone.Should().Be(ZoneType.Hand);
        stack.Count.Should().Be(0);
    }

    /// <summary>Minimal agent: records when targets are asked and returns the
    /// canned pick; queues nothing else (no other prompt should fire).</summary>
    private sealed class TargetRecordingAgent : IPlayerAgent
    {
        private readonly List<string> _events;
        private readonly IReadOnlyList<object> _pick;

        public TargetRecordingAgent(List<string> events, IReadOnlyList<object> pick)
        {
            _events = events;
            _pick = pick;
        }

        public Task<IReadOnlyList<object>> ChooseTargetsAsync(
            GameContext ctx, TargetRequest request, CancellationToken ct = default)
        {
            _events.Add("targets");
            return Task.FromResult(_pick);
        }

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => Task.FromResult(PriorityAction.Pass);
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => Task.FromResult(MulliganDecision.Keep);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ICard>>(Array.Empty<ICard>());
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => Task.FromResult(ManaPayment.Empty);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> abilities, CancellationToken ct = default)
            => Task.FromResult(abilities);
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligible, CancellationToken ct = default)
            => Task.FromResult(new CombatPlan(Array.Empty<Majik.Core.Players.Agents.AttackerDeclaration>()));
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligible, CancellationToken ct = default)
            => Task.FromResult(new BlockPlan(Array.Empty<Majik.Core.Players.Agents.BlockerDeclaration>()));
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.ScryAction.ScryDecision(peeked, Array.Empty<ICard>()));
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.SurveilAction.SurveilDecision(peeked, Array.Empty<ICard>()));
    }

    /// <summary>
    /// Wraps <see cref="MainPhaseCastAgent"/> but floats {R} into the pool
    /// immediately before proposing the cast, so the auto-pay-from-pool
    /// short-circuit (no ChooseManaSourcesAsync prompt) is exercised. The
    /// float happens here — not in test arrange — because the turn's untap/
    /// upkeep/draw steps empty mana pools before the main phase.
    /// </summary>
    private sealed class PoolFloatingMainPhaseCastAgent : IPlayerAgent
    {
        private readonly MainPhaseCastAgent _innerCastAgent;
        private readonly Player _self;
        private bool _floated;

        /// <summary>Pool snapshot taken at the first priority window AFTER
        /// the cast was proposed — i.e. after the dispatch failed. Pools are
        /// swept at cleanup, so this is the observable refund surface.</summary>
        public Majik.Core.ValueObjects.ManaPool? PoolAfterCastAttempt { get; private set; }

        public PoolFloatingMainPhaseCastAgent(ScriptedAgent inner, ICard card, Player self)
        {
            _innerCastAgent = new MainPhaseCastAgent(inner, card, self);
            _self = self;
        }

        public async Task<PriorityAction> ChoosePriorityActionAsync(
            GameContext ctx, CancellationToken ct = default)
        {
            if (_floated && PoolAfterCastAttempt == null)
            {
                PoolAfterCastAttempt = _self.ManaPool;
            }
            var action = await _innerCastAgent.ChoosePriorityActionAsync(ctx, ct);
            if (!_floated && action is PriorityAction.CastSpell)
            {
                _self.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("{R}"));
                _floated = true;
            }
            return action;
        }

        public Task<IReadOnlyList<object>> ChooseAsync(GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
            => _innerCastAgent.ChooseAsync(ctx, req, ct);
        public Task<bool> ChooseYesNoAsync(string question, BotIntent intent, CancellationToken ct = default)
            => _innerCastAgent.ChooseYesNoAsync(question, intent, ct);
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => _innerCastAgent.ChooseMulliganAsync(ctx, hand, mulligansTaken, ct);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => _innerCastAgent.ChooseCardsToBottomAsync(ctx, hand, countToBottom, ct);
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => _innerCastAgent.ChooseTargetsAsync(ctx, request, ct);
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
            => _innerCastAgent.ChooseXAsync(ctx, source, ct);
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => _innerCastAgent.ChooseModeAsync(ctx, modes, modeIntents, ct);
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => _innerCastAgent.ChooseManaSourcesAsync(ctx, cost, ct);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> abilities, CancellationToken ct = default)
            => _innerCastAgent.OrderTriggersAsync(ctx, abilities, ct);
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligible, CancellationToken ct = default)
            => _innerCastAgent.DeclareAttackersAsync(ctx, eligible, ct);
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligible, CancellationToken ct = default)
            => _innerCastAgent.DeclareBlockersAsync(ctx, attackers, eligible, ct);
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => _innerCastAgent.ChooseScryDecisionAsync(ctx, peeked, ct);
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => _innerCastAgent.ChooseSurveilDecisionAsync(ctx, peeked, ct);
    }
}
