using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// Exhaustive battery for Slice 5a's server-side auto-pass gate inside
/// <see cref="PriorityLoop"/>. Validates each leg of the
/// <see cref="PriorityLoop.TryAutoPass"/> conjunction independently so a
/// regression in any one falls out loudly.
///
/// <para>Test harness: instead of a full GameFacade, we drive the loop
/// directly with a <see cref="ProbeAgent"/> that records every
/// ChoosePriorityActionAsync call. The auto-pass path must
/// SKIP the agent entirely → call count stays at zero. The prompt path
/// MUST reach the agent → call count is non-zero.</para>
/// </summary>
public class PriorityLoopAutoPassTests
{
    private sealed class TestPrefs : IAutoPassPrefsView
    {
        public bool FullControl { get; init; }
        public IReadOnlyDictionary<string, string> PhaseStops { get; init; }
            = new Dictionary<string, string>();
    }

    /// <summary>
    /// Agent that records the count of ChoosePriorityActionAsync calls.
    /// Lets each test assert whether the agent was consulted (= auto-pass
    /// missed) or skipped (= auto-pass fired). All other prompt kinds
    /// delegate to a DeterministicBotAgent — none should fire in these
    /// tests since the loop is driven against an empty board.
    /// </summary>
    private sealed class CountingAgent : IPlayerAgent
    {
        private readonly DeterministicBotAgent _inner = new();
        public int PromptCount { get; private set; }

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
        {
            PromptCount++;
            return Task.FromResult(PriorityAction.Pass);
        }

        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, System.Collections.Generic.IReadOnlyList<Majik.Core.Cards.ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => _inner.ChooseMulliganAsync(ctx, hand, mulligansTaken, ct);
        public Task<System.Collections.Generic.IReadOnlyList<Majik.Core.Cards.ICard>> ChooseCardsToBottomAsync(GameContext ctx, System.Collections.Generic.IReadOnlyList<Majik.Core.Cards.ICard> hand, int countToBottom, CancellationToken ct = default)
            => _inner.ChooseCardsToBottomAsync(ctx, hand, countToBottom, ct);
        public Task<System.Collections.Generic.IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, Majik.Core.Players.Agents.TargetRequest request, CancellationToken ct = default)
            => _inner.ChooseTargetsAsync(ctx, request, ct);
        public Task<int> ChooseXAsync(GameContext ctx, Majik.Core.Cards.ICard source, CancellationToken ct = default)
            => _inner.ChooseXAsync(ctx, source, ct);
        public Task<int> ChooseModeAsync(GameContext ctx, System.Collections.Generic.IReadOnlyList<string> modes, System.Collections.Generic.IReadOnlyList<Majik.Core.Cards.BotIntent>? modeIntents = null, CancellationToken ct = default)
            => _inner.ChooseModeAsync(ctx, modes, modeIntents, ct);
        public Task<System.Collections.Generic.IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, System.Collections.Generic.IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility> mine, CancellationToken ct = default)
            => _inner.OrderTriggersAsync(ctx, mine, ct);
        public Task<Majik.Core.Players.Agents.ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => _inner.ChooseManaSourcesAsync(ctx, cost, ct);
        public Task<Majik.Core.Players.Agents.CombatPlan> DeclareAttackersAsync(GameContext ctx, System.Collections.Generic.IReadOnlyList<Majik.Core.Cards.Creature> eligibleAttackers, CancellationToken ct = default)
            => _inner.DeclareAttackersAsync(ctx, eligibleAttackers, ct);
        public Task<Majik.Core.Players.Agents.BlockPlan> DeclareBlockersAsync(GameContext ctx, System.Collections.Generic.IReadOnlyList<Majik.Core.Cards.Creature> attackers, System.Collections.Generic.IReadOnlyList<Majik.Core.Cards.Creature> eligibleBlockers, CancellationToken ct = default)
            => _inner.DeclareBlockersAsync(ctx, attackers, eligibleBlockers, ct);
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, System.Collections.Generic.IReadOnlyList<Majik.Core.Cards.ICard> peeked, CancellationToken ct = default)
            => _inner.ChooseScryDecisionAsync(ctx, peeked, ct);
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, System.Collections.Generic.IReadOnlyList<Majik.Core.Cards.ICard> peeked, CancellationToken ct = default)
            => _inner.ChooseSurveilDecisionAsync(ctx, peeked, ct);
    }

    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;
    private readonly PriorityManager _priority;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public PriorityLoopAutoPassTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _priority = new PriorityManager(new System.Collections.Generic.List<Player> { _alice, _bob }, _stack, _bus, _triggers);
        _resolver = new StackResolver(_bus);
    }

    private PriorityLoop NewLoop(
        CountingAgent aliceAgent, CountingAgent bobAgent,
        System.Func<Player, IAutoPassPrefsView?>? prefs = null,
        System.Func<GameContext, bool>? deadWindow = null,
        IEventBus? eventBus = null,
        System.Func<System.DateTime>? clock = null,
        PhaseStateType phase = PhaseStateType.PreCombatMain)
    {
        var agents = new System.Collections.Generic.Dictionary<Player, IPlayerAgent>
        {
            [_alice] = aliceAgent,
            [_bob] = bobAgent,
        };
        return new PriorityLoop(
            players: new[] { _alice, _bob },
            priority: _priority,
            stack: _stack,
            stackResolver: _resolver,
            zoneService: new ZoneService(_bus),
            agents: agents,
            turnNumberAccessor: () => 1,
            phaseAccessor: () => phase,
            landDropTracker: new LandDropTracker(),
            autoPassPrefsProvider: prefs,
            isPassOnlyDeadWindow: deadWindow,
            eventBus: eventBus,
            clock: clock);
    }

    // -------------------------------------------------------------------
    // Gate 1 — dead-window detection
    // -------------------------------------------------------------------

    [Fact]
    public async Task DeadWindow_DefaultPrefs_AutoPasses_NoPrompt()
    {
        var alice = new CountingAgent();
        var bob = new CountingAgent();
        var loop = NewLoop(alice, bob,
            prefs: _ => new TestPrefs(),
            deadWindow: _ => true,
            // Clock fixed at a time well past DateTime.MinValue so the
            // stack-display window is satisfied. (No bus → mutations
            // never advance _lastStackMutatedAt past MinValue anyway,
            // but pin it explicitly.)
            clock: () => new System.DateTime(2026, 1, 1));

        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().Be(0, "the engine auto-passed both windows server-side");
        bob.PromptCount.Should().Be(0);
        _stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task PromptCountIsNonZero_WithoutAutoPassWiring()
    {
        // Sanity: when prefs + deadWindow are NOT wired, the loop falls
        // back to its pre-Slice-5a behaviour and prompts both agents.
        var alice = new CountingAgent();
        var bob = new CountingAgent();
        var loop = NewLoop(alice, bob);

        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().BeGreaterThan(0);
        bob.PromptCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task NonDeadWindow_PromptsAgent()
    {
        // PriorityKinds.Build returned >1 kind (e.g. PlayLand offered).
        var alice = new CountingAgent();
        var bob = new CountingAgent();
        var loop = NewLoop(alice, bob,
            prefs: _ => new TestPrefs(),
            deadWindow: _ => false,
            clock: () => new System.DateTime(2026, 1, 1));

        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().BeGreaterThan(0);
    }

    // -------------------------------------------------------------------
    // Gate 2 — bot seats (prefs provider returns null)
    // -------------------------------------------------------------------

    [Fact]
    public async Task PrefsProviderReturnsNullForBotSeat_PromptsAgent()
    {
        // Simulate bot-on-alice, human-on-bob — prefs for alice is null,
        // for bob is non-null. Both windows are otherwise dead.
        var alice = new CountingAgent();
        var bob = new CountingAgent();
        var loop = NewLoop(alice, bob,
            prefs: p => ReferenceEquals(p, _alice) ? null : new TestPrefs(),
            deadWindow: _ => true,
            clock: () => new System.DateTime(2026, 1, 1));

        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().BeGreaterThan(0, "alice's prefs are null → bot path drives itself");
        bob.PromptCount.Should().Be(0, "bob's prefs allowed auto-pass on the dead window");
    }

    // -------------------------------------------------------------------
    // Gate 3 — FullControl
    // -------------------------------------------------------------------

    [Fact]
    public async Task FullControl_True_PromptsAgent_EvenOnDeadWindow()
    {
        var alice = new CountingAgent();
        var bob = new CountingAgent();
        var loop = NewLoop(alice, bob,
            prefs: _ => new TestPrefs { FullControl = true },
            deadWindow: _ => true,
            clock: () => new System.DateTime(2026, 1, 1));

        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().BeGreaterThan(0);
        bob.PromptCount.Should().BeGreaterThan(0);
    }

    // -------------------------------------------------------------------
    // Gate 4 — phase stops
    // -------------------------------------------------------------------

    [Fact]
    public async Task PhaseStop_OnActiveSide_PromptsAgent()
    {
        // Alice is active; her viewer's PhaseStops says "PreCombatMain →
        // mine" → stop on HER own turn → suppress auto-pass for alice.
        // Bob (defender) has no stop → auto-pass for bob (the active side
        // from bob's POV is "theirs").
        var alice = new CountingAgent();
        var bob = new CountingAgent();
        var loop = NewLoop(alice, bob,
            prefs: p => ReferenceEquals(p, _alice)
                ? new TestPrefs { PhaseStops = new System.Collections.Generic.Dictionary<string, string> { ["PreCombatMain"] = "mine" } }
                : new TestPrefs(),
            deadWindow: _ => true,
            clock: () => new System.DateTime(2026, 1, 1),
            phase: PhaseStateType.PreCombatMain);

        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().BeGreaterThan(0, "alice's stop fires on her own turn");
        bob.PromptCount.Should().Be(0, "bob has no stop → auto-pass on the dead window");
    }

    [Fact]
    public async Task PhaseStop_OnOppositeSide_StillAutoPasses()
    {
        // Alice is active; ONLY Alice carries the "PreCombatMain → theirs"
        // stop — which fires on Alice's OPPONENT's turn, not Alice's own.
        // We're on Alice's turn ("mine" for Alice), so the stop does not
        // fire for Alice. Bob has default prefs (no stops) and gets
        // auto-passed normally. Both windows auto-pass.
        var alice = new CountingAgent();
        var bob = new CountingAgent();
        var loop = NewLoop(alice, bob,
            prefs: p => ReferenceEquals(p, _alice)
                ? new TestPrefs { PhaseStops = new System.Collections.Generic.Dictionary<string, string> { ["PreCombatMain"] = "theirs" } }
                : new TestPrefs(),
            deadWindow: _ => true,
            clock: () => new System.DateTime(2026, 1, 1),
            phase: PhaseStateType.PreCombatMain);

        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().Be(0);
        bob.PromptCount.Should().Be(0);
    }

    [Fact]
    public async Task PhaseStop_DifferentPhaseLabel_StillAutoPasses()
    {
        // Stop set on "Upkeep" but we're in PreCombatMain → no stop for
        // this window → auto-pass.
        var alice = new CountingAgent();
        var bob = new CountingAgent();
        var loop = NewLoop(alice, bob,
            prefs: _ => new TestPrefs
            {
                PhaseStops = new System.Collections.Generic.Dictionary<string, string> { ["Upkeep"] = "mine" }
            },
            deadWindow: _ => true,
            clock: () => new System.DateTime(2026, 1, 1),
            phase: PhaseStateType.PreCombatMain);

        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().Be(0);
    }

    // -------------------------------------------------------------------
    // Gate 5 — stack-mutation display window
    // -------------------------------------------------------------------

    [Fact]
    public async Task WithinStackDisplayWindow_PromptsAgent()
    {
        // Synthesise a stack mutation immediately before running the loop;
        // clock barely advances → still inside the display window → prompt.
        var alice = new CountingAgent();
        var bob = new CountingAgent();
        var now = new System.DateTime(2026, 1, 1);

        // Construct the loop FIRST (subscribes to the bus), then publish.
        var loop = NewLoop(alice, bob,
            prefs: _ => new TestPrefs(),
            deadWindow: _ => true,
            eventBus: _bus,
            clock: () => now,
            phase: PhaseStateType.PreCombatMain);

        // Stamp the stack as just-mutated.
        var permanent = new Majik.Core.Cards.Creature("Goblin", "R", 1, 1) { Owner = _alice, Zone = ZoneType.Battlefield };
        var trig = new Majik.Core.Abilities.TriggeredAbility(
            permanent, _alice,
            Majik.Core.Abilities.Triggers.OnEnterBattlefieldSelf(permanent),
            effects: System.Array.Empty<Majik.Core.Abilities.IEffect>());
        _bus.Publish(new StackObjectAddedEvent(trig));

        // Still at `now` — zero ms elapsed since the publish.
        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().BeGreaterThan(0, "we're still inside the stack-display window");
    }

    [Fact]
    public async Task AfterStackDisplayWindowElapses_AutoPasses()
    {
        // Publish a stack mutation, then advance the clock past the
        // display window → auto-pass.
        var alice = new CountingAgent();
        var bob = new CountingAgent();
        var now = new System.DateTime(2026, 1, 1);

        var loop = NewLoop(alice, bob,
            prefs: _ => new TestPrefs(),
            deadWindow: _ => true,
            eventBus: _bus,
            clock: () => now,
            phase: PhaseStateType.PreCombatMain);

        var permanent = new Majik.Core.Cards.Creature("Goblin", "R", 1, 1) { Owner = _alice, Zone = ZoneType.Battlefield };
        var trig = new Majik.Core.Abilities.TriggeredAbility(
            permanent, _alice,
            Majik.Core.Abilities.Triggers.OnEnterBattlefieldSelf(permanent),
            effects: System.Array.Empty<Majik.Core.Abilities.IEffect>());
        _bus.Publish(new StackObjectAddedEvent(trig));

        // Jump past the display window.
        now = now.AddMilliseconds(AutoPassConstants.StackMutationDisplayMs + 50);

        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().Be(0);
        bob.PromptCount.Should().Be(0);
    }

    [Fact]
    public async Task StackObjectResolved_AlsoStampsMutation()
    {
        // Symmetric to StackObjectAdded — resolution counts as a visible
        // stack mutation from the player's POV (the trigger LEAVES the
        // stack), so the display window must apply afterwards too.
        var alice = new CountingAgent();
        var bob = new CountingAgent();
        var now = new System.DateTime(2026, 1, 1);

        var loop = NewLoop(alice, bob,
            prefs: _ => new TestPrefs(),
            deadWindow: _ => true,
            eventBus: _bus,
            clock: () => now,
            phase: PhaseStateType.PreCombatMain);

        var permanent = new Majik.Core.Cards.Creature("Goblin", "R", 1, 1) { Owner = _alice, Zone = ZoneType.Battlefield };
        var trig = new Majik.Core.Abilities.TriggeredAbility(
            permanent, _alice,
            Majik.Core.Abilities.Triggers.OnEnterBattlefieldSelf(permanent),
            effects: System.Array.Empty<Majik.Core.Abilities.IEffect>());
        _bus.Publish(new StackObjectResolvedEvent(trig));

        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().BeGreaterThan(0, "within display window after resolution");
    }

    // -------------------------------------------------------------------
    // Bus subscription hygiene
    // -------------------------------------------------------------------

    [Fact]
    public void DetachFromBus_RemovesHandlers()
    {
        var alice = new CountingAgent();
        var bob = new CountingAgent();
        var loop = NewLoop(alice, bob,
            prefs: _ => new TestPrefs(),
            deadWindow: _ => true,
            eventBus: _bus,
            clock: () => System.DateTime.UtcNow);

        loop.DetachFromBus();
        loop.DetachFromBus(); // idempotent — safe to call twice

        // No assertion target on the bus surface; this test guards against
        // future regressions in DetachFromBus throwing on a missing
        // handler (Unsubscribe is tolerant of unknown handlers).
    }
}
