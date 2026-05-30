using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

/// <summary>
/// CR 720 ("Controlling Another Player") — the ControlPlayer primitive.
/// Mindslaver / Emrakul, the Promised End grant one player control of
/// another player's next turn: during that turn the controller makes every
/// decision the controlled player would normally make (CR 720.1), but the
/// controlled player's cards, hand, life, and library remain theirs
/// (CR 720.2 / CR 720.3 — only decision-making is reassigned).
///
/// These tests pin the routing primitive in isolation: a registry of grants,
/// the agent-map view that reroutes decisions, and an end-to-end scripted
/// turn where the controller's agent plays the controlled player's land.
/// </summary>
public class ControlPlayerTests
{
    // -----------------------------------------------------------------
    // Registry / agent-map routing (unit)
    // -----------------------------------------------------------------

    [Fact]
    public void Registry_NoGrant_EffectiveDecisionMakerIsThePlayerThemselves()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var reg = new ControlPlayerRegistry();

        reg.HasActiveControl.Should().BeFalse();
        reg.EffectiveDecisionMaker(bob).Should().BeSameAs(bob);
        reg.EffectiveDecisionMaker(alice).Should().BeSameAs(alice);
    }

    [Fact]
    public void Registry_GrantThenConsume_RoutesControlledPlayerToController()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var reg = new ControlPlayerRegistry();

        // CR 720.1 — Alice gains control of Bob's next turn.
        reg.GrantControl(controller: alice, controlled: bob);
        reg.HasPendingControl(bob).Should().BeTrue();
        // Not active until Bob's turn actually starts.
        reg.HasActiveControl.Should().BeFalse();
        reg.EffectiveDecisionMaker(bob).Should().BeSameAs(bob);

        // Bob's turn starts → consume the grant.
        reg.ConsumeControlFor(bob, out var controller).Should().BeTrue();
        controller.Should().BeSameAs(alice);
        reg.HasActiveControl.Should().BeTrue();
        reg.ActivelyControlled.Should().BeSameAs(bob);
        reg.ActiveController.Should().BeSameAs(alice);

        // CR 720.1 — Bob's decisions now route to Alice; Alice still makes
        // her own.
        reg.EffectiveDecisionMaker(bob).Should().BeSameAs(alice);
        reg.EffectiveDecisionMaker(alice).Should().BeSameAs(alice);

        // The grant is one-shot — consuming removed it from the pending set.
        reg.HasPendingControl(bob).Should().BeFalse();

        // Turn ends → control reverts.
        reg.ClearActiveControl();
        reg.HasActiveControl.Should().BeFalse();
        reg.EffectiveDecisionMaker(bob).Should().BeSameAs(bob);
    }

    [Fact]
    public void Registry_CannotControlSelf()
    {
        var alice = new Player("Alice", 20);
        var reg = new ControlPlayerRegistry();

        reg.GrantControl(controller: alice, controlled: alice);

        reg.HasPendingControl(alice).Should().BeFalse();
    }

    [Fact]
    public void AgentMap_WhenControlActive_IndexerReturnsControllersAgent()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var aliceAgent = new DeterministicBotAgent();
        var bobAgent = new DeterministicBotAgent();
        var reg = new ControlPlayerRegistry();
        var map = new ControlAwareAgentMap(
            new Dictionary<Player, IPlayerAgent> { [alice] = aliceAgent, [bob] = bobAgent },
            reg);

        // No control: each seat maps to its own agent.
        map[alice].Should().BeSameAs(aliceAgent);
        map[bob].Should().BeSameAs(bobAgent);

        reg.GrantControl(alice, bob);
        reg.ConsumeControlFor(bob, out _);

        // CR 720.1 — Bob's decisions route to Alice's agent.
        map[bob].Should().BeSameAs(aliceAgent);
        map[alice].Should().BeSameAs(aliceAgent);
        // TryGetValue honours control too.
        map.TryGetValue(bob, out var viaTry).Should().BeTrue();
        viaTry.Should().BeSameAs(aliceAgent);

        reg.ClearActiveControl();
        map[bob].Should().BeSameAs(bobAgent);
    }

    // -----------------------------------------------------------------
    // End-to-end: controller's agent makes the controlled player's plays
    // -----------------------------------------------------------------

    /// <summary>
    /// CR 720.1 — during a controlled turn the controller's agent is the one
    /// solicited for the controlled player's priority decisions. Here it's
    /// Bob's turn but Alice controls him; Alice's scripted agent plays a land
    /// out of BOB's hand. The land (Bob's card — CR 720.2) ends up on Bob's
    /// battlefield, proving Alice's agent drove Bob's play. Bob's own agent
    /// only ever passes, so if control weren't routed the land would stay in
    /// hand.
    /// </summary>
    [Fact]
    public async Task ControlledTurn_ControllersAgentPlaysControlledPlayersLand()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var players = new List<Player> { alice, bob };
        var priority = new PriorityManager(players, stack, bus, triggers);
        var tracker = new LandDropTracker();

        // Bob (the controlled player) has a land in hand.
        var bobsLand = NamedCardFactory.Create("Mountain", bob);
        bobsLand.SetZone(ZoneType.Hand);
        bob.Zones.Hand.AddCard(bobsLand);

        foreach (var p in players)
        {
            for (var i = 0; i < 5; i++)
            {
                var c = NamedCardFactory.Create("Mountain", p);
                p.Zones.Library.AddCard(c);
                c.SetZone(ZoneType.Library);
            }
        }

        // Alice's agent will, when asked to act for the active player (Bob,
        // under control), play Bob's land. Bob's own agent always passes.
        var aliceAgent = new PlayActivePlayersLandAgent();
        var bobAgent = new AlwaysPassAgent();

        var reg = new ControlPlayerRegistry();
        var baseAgents = new Dictionary<Player, IPlayerAgent>
        {
            [alice] = aliceAgent,
            [bob] = bobAgent,
        };
        var controlAware = new ControlAwareAgentMap(baseAgents, reg);

        var driver = new TurnDriver(
            players, controlAware, stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            continuousEffects: null,
            eventBus: bus,
            landDropTracker: tracker,
            controlRegistry: reg);

        // CR 720.1 — Alice gains control of Bob's next turn.
        reg.GrantControl(controller: alice, controlled: bob);

        // Run Bob's (controlled) turn.
        await driver.RunTurnAsync(bob, turnNumber: 2);

        // Alice's agent drove Bob's priority — it was invoked, and Bob's own
        // pass-only agent was never asked for a priority decision on Bob's
        // turn (every Bob-seat lookup rerouted to Alice).
        aliceAgent.PriorityCalls.Should().BeGreaterThan(0);
        bobAgent.PriorityCalls.Should().Be(0);

        // CR 720.2 — a land entered BOB's battlefield (the controller drove
        // the play out of Bob's hand), and it is BOB's card under BOB's
        // control — control only moved the decision, not ownership. (The
        // exact card may be the turn-2 drawn Mountain rather than the
        // hand-seeded one; both are Bob's, which is the point.)
        var bobLandsOnField = bob.Zones.Battlefield.GetCards()
            .Where(c => c.HasType(Majik.Core.Cards.Types.CardType.Land))
            .ToList();
        bobLandsOnField.Should().ContainSingle("CR 305.2 — one land drop on Bob's turn");
        bobLandsOnField[0].Owner.Should().BeSameAs(bob);
        bobLandsOnField[0].Controller.Should().BeSameAs(bob);
        // Alice (the controller) gained nothing on her own battlefield.
        alice.Zones.Battlefield.GetCards()
            .Should().NotContain(c => c.HasType(Majik.Core.Cards.Types.CardType.Land));

        // CR 720.1 — control reverts when the turn ends.
        reg.HasActiveControl.Should().BeFalse();
    }

    /// <summary>
    /// CR 720.1 — control lasts exactly one turn. After Bob's controlled turn
    /// ends, Bob's own agent makes his decisions again on his following turn.
    /// </summary>
    [Fact]
    public async Task ControlReverts_BobsNextOwnTurn_BobsAgentDecidesAgain()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var players = new List<Player> { alice, bob };
        var priority = new PriorityManager(players, stack, bus, triggers);
        var tracker = new LandDropTracker();

        foreach (var p in players)
        {
            for (var i = 0; i < 8; i++)
            {
                var c = NamedCardFactory.Create("Mountain", p);
                p.Zones.Library.AddCard(c);
                c.SetZone(ZoneType.Library);
            }
        }

        var aliceAgent = new PlayActivePlayersLandAgent();
        var bobAgent = new AlwaysPassAgent();
        var reg = new ControlPlayerRegistry();
        var controlAware = new ControlAwareAgentMap(
            new Dictionary<Player, IPlayerAgent> { [alice] = aliceAgent, [bob] = bobAgent },
            reg);

        var driver = new TurnDriver(
            players, controlAware, stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            continuousEffects: null,
            eventBus: bus,
            landDropTracker: tracker,
            controlRegistry: reg);

        reg.GrantControl(controller: alice, controlled: bob);
        await driver.RunTurnAsync(bob, turnNumber: 2);
        // During the controlled turn Bob's own agent is never solicited —
        // every Bob-seat lookup rerouted to Alice.
        bobAgent.PriorityCalls.Should().Be(0);

        // Bob's NEXT own turn — no grant pending, so Bob makes his own
        // decisions again (CR 720.1 — control lasts a single turn).
        await driver.RunTurnAsync(bob, turnNumber: 4);

        // CR 720.1 — control has reverted: Bob's agent is now the one
        // solicited for Bob's priority. (Alice's agent is still legitimately
        // called for ALICE's own priority windows on Bob's turn — she's the
        // opponent — so we don't assert her call count is frozen.)
        bobAgent.PriorityCalls.Should().BeGreaterThan(0);
        reg.HasActiveControl.Should().BeFalse();
    }

    // -----------------------------------------------------------------
    // Test agents
    // -----------------------------------------------------------------

    /// <summary>
    /// Base test agent: forwards every decision to an inner
    /// <see cref="DeterministicBotAgent"/> (so the dozens of IPlayerAgent
    /// members behave) and lets a subclass override only the priority
    /// decision. ScriptedAgent / DeterministicBotAgent are both sealed, so
    /// composition is the only seam.
    /// </summary>
    private abstract class DelegatingAgent : IPlayerAgent
    {
        protected readonly DeterministicBotAgent Inner = new();

        public abstract Task<PriorityAction> ChoosePriorityActionAsync(
            GameContext ctx, CancellationToken ct = default);

        public Task<MulliganDecision> ChooseMulliganAsync(
            GameContext ctx, IReadOnlyList<Majik.Core.Cards.ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => Inner.ChooseMulliganAsync(ctx, hand, mulligansTaken, ct);

        public Task<IReadOnlyList<Majik.Core.Cards.ICard>> ChooseCardsToBottomAsync(
            GameContext ctx, IReadOnlyList<Majik.Core.Cards.ICard> hand, int countToBottom, CancellationToken ct = default)
            => Inner.ChooseCardsToBottomAsync(ctx, hand, countToBottom, ct);

        public Task<IReadOnlyList<object>> ChooseTargetsAsync(
            GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => Inner.ChooseTargetsAsync(ctx, request, ct);

        public Task<int> ChooseXAsync(GameContext ctx, Majik.Core.Cards.ICard source, CancellationToken ct = default)
            => Inner.ChooseXAsync(ctx, source, ct);

        public Task<int> ChooseModeAsync(
            GameContext ctx, IReadOnlyList<string> modes,
            IReadOnlyList<Majik.Core.Cards.BotIntent>? modeIntents = null, CancellationToken ct = default)
            => Inner.ChooseModeAsync(ctx, modes, modeIntents, ct);

        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(
            GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => Inner.OrderTriggersAsync(ctx, mine, ct);

        public Task<ManaPayment> ChooseManaSourcesAsync(
            GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => Inner.ChooseManaSourcesAsync(ctx, cost, ct);

        public Task<CombatPlan> DeclareAttackersAsync(
            GameContext ctx, IReadOnlyList<Majik.Core.Cards.Creature> eligibleAttackers, CancellationToken ct = default)
            => Inner.DeclareAttackersAsync(ctx, eligibleAttackers, ct);

        public Task<BlockPlan> DeclareBlockersAsync(
            GameContext ctx, IReadOnlyList<Majik.Core.Cards.Creature> attackers,
            IReadOnlyList<Majik.Core.Cards.Creature> eligibleBlockers, CancellationToken ct = default)
            => Inner.DeclareBlockersAsync(ctx, attackers, eligibleBlockers, ct);

        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(
            GameContext? ctx, IReadOnlyList<Majik.Core.Cards.ICard> peeked, CancellationToken ct = default)
            => Inner.ChooseScryDecisionAsync(ctx, peeked, ct);

        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(
            GameContext? ctx, IReadOnlyList<Majik.Core.Cards.ICard> peeked, CancellationToken ct = default)
            => Inner.ChooseSurveilDecisionAsync(ctx, peeked, ct);
    }

    /// <summary>
    /// Agent that plays the ACTIVE player's first land in hand (once), then
    /// passes. Used as the controller's agent: when it's asked to act for the
    /// controlled (active) player, it plays that player's land — proving the
    /// controller is making the controlled player's decisions.
    /// </summary>
    private sealed class PlayActivePlayersLandAgent : DelegatingAgent
    {
        public int PriorityCalls { get; private set; }
        private readonly HashSet<System.Guid> _playedFor = new();

        public override Task<PriorityAction> ChoosePriorityActionAsync(
            GameContext ctx, CancellationToken ct = default)
        {
            PriorityCalls++;
            // Only act on the active player's own MAIN-phase priority window
            // (CR 305.1 — a land can only be played during a main phase with
            // an empty stack while you have priority; proposing earlier would
            // be rejected and waste the one-shot).
            var inMain = ctx.CurrentPhase is Majik.Core.StateMachine.PhaseStateType.PreCombatMain
                or Majik.Core.StateMachine.PhaseStateType.PostCombatMain;
            if (inMain
                && ReferenceEquals(ctx.Self, ctx.ActivePlayer)
                && !_playedFor.Contains(ctx.Self.Id))
            {
                var land = ctx.Self.Zones.Hand.GetCards()
                    .FirstOrDefault(c => c.HasType(Majik.Core.Cards.Types.CardType.Land));
                if (land != null)
                {
                    _playedFor.Add(ctx.Self.Id);
                    return Task.FromResult<PriorityAction>(new PriorityAction.PlayLand(land));
                }
            }
            return Task.FromResult<PriorityAction>(PriorityAction.Pass);
        }
    }

    /// <summary>Agent that always passes priority and counts how often it was
    /// asked.</summary>
    private sealed class AlwaysPassAgent : DelegatingAgent
    {
        public int PriorityCalls { get; private set; }

        public override Task<PriorityAction> ChoosePriorityActionAsync(
            GameContext ctx, CancellationToken ct = default)
        {
            PriorityCalls++;
            return Task.FromResult<PriorityAction>(PriorityAction.Pass);
        }
    }
}
