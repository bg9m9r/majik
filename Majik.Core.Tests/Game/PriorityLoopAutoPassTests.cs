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
        public Task<Majik.Core.Players.Agents.CombatPlan> DeclareAttackersAsync(GameContext ctx, System.Collections.Generic.IReadOnlyList<Majik.Core.Cards.Permanent> eligibleAttackers, CancellationToken ct = default)
            => _inner.DeclareAttackersAsync(ctx, eligibleAttackers, ct);
        public Task<Majik.Core.Players.Agents.BlockPlan> DeclareBlockersAsync(GameContext ctx, System.Collections.Generic.IReadOnlyList<Majik.Core.Cards.Permanent> attackers, System.Collections.Generic.IReadOnlyList<Majik.Core.Cards.Permanent> eligibleBlockers, CancellationToken ct = default)
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
        StepStateType phase = StepStateType.PreCombatMain)
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
            isPassOnlyDeadWindow: deadWindow);
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
            deadWindow: _ => true);

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
            deadWindow: _ => false);

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
            deadWindow: _ => true);

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
            deadWindow: _ => true);

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
            phase: StepStateType.PreCombatMain);

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
            phase: StepStateType.PreCombatMain);

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
            phase: StepStateType.PreCombatMain);

        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().Be(0);
    }

    // -------------------------------------------------------------------
    // Dead-window auto-pass is IMMEDIATE — no server-side stack-display beat
    //
    // The engine must NEVER block awaiting a human on a pass-only window.
    // A prior "Gate 5" suppressed auto-pass for a beat after any stack
    // mutation; because own-top is already exempt, that gate fired ONLY on
    // the dead, not-own-top case — exactly the window where blocking on the
    // human wedged a live human-vs-bot match permanently (replay-confirmed:
    // bot cast Boltwave → human got a dead window on an empty board → the
    // loop awaited a pass the client never surfaced → frozen forever). The
    // gate was removed; these tests prove a dead window auto-passes
    // immediately regardless of how recently the stack mutated. The
    // minimum-display beat remains purely client-side in the portal.
    // -------------------------------------------------------------------

    // THE WEDGE-FIX TEST. Was `WithinStackDisplayWindow_PromptsAgent`, which
    // asserted the OLD buggy behaviour: a dead window falling inside the
    // post-stack-mutation display beat was SUPPRESSED from auto-pass and the
    // agent was prompted. That encoded the wedge — on a human seat whose only
    // legal move is pass, prompting blocks the loop forever when the client
    // never surfaces an actionable pass-only prompt. Flipped to assert the
    // correct behaviour: a dead, not-own-top window ALWAYS auto-passes,
    // immediately, no matter how recently the stack mutated.
    [Fact]
    public async Task DeadWindow_ImmediatelyAfterStackMutation_StillAutoPasses_NoPrompt()
    {
        var alice = new CountingAgent();
        var bob = new CountingAgent();

        var loop = NewLoop(alice, bob,
            prefs: _ => new TestPrefs(),
            deadWindow: _ => true,
            phase: StepStateType.PreCombatMain);

        // Publish a stack-mutation event in the same beat we run the loop —
        // the very situation the removed Gate 5 suppressed auto-pass for.
        // With the fix there is no stack-display beat server-side, so the
        // dead window auto-passes regardless. (No bus is wired into the loop
        // any more; this publish is a no-op for it and merely documents the
        // "fresh mutation" intent.)
        var permanent = new Majik.Core.Cards.Creature("Goblin", "R", 1, 1) { Owner = _alice, Zone = ZoneType.Battlefield };
        var trig = new Majik.Core.Abilities.TriggeredAbility(
            permanent, _alice,
            Majik.Core.Abilities.Triggers.OnEnterBattlefieldSelf(permanent),
            effects: System.Array.Empty<Majik.Core.Abilities.IEffect>());
        _bus.Publish(new StackObjectAddedEvent(trig));

        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().Be(0,
            "a dead (pass-only) window must auto-pass immediately even right after " +
            "a stack mutation — never block awaiting a human whose only move is pass");
        bob.PromptCount.Should().Be(0);
    }

    // Was `StackObjectResolved_AlsoStampsMutation`, which asserted the OLD
    // buggy behaviour for the resolution side of the beat (a freshly-resolved
    // object suppressed auto-pass and prompted). Flipped: a dead window right
    // after a resolution event still auto-passes.
    [Fact]
    public async Task DeadWindow_ImmediatelyAfterStackResolution_StillAutoPasses_NoPrompt()
    {
        var alice = new CountingAgent();
        var bob = new CountingAgent();

        var loop = NewLoop(alice, bob,
            prefs: _ => new TestPrefs(),
            deadWindow: _ => true,
            phase: StepStateType.PreCombatMain);

        var permanent = new Majik.Core.Cards.Creature("Goblin", "R", 1, 1) { Owner = _alice, Zone = ZoneType.Battlefield };
        var trig = new Majik.Core.Abilities.TriggeredAbility(
            permanent, _alice,
            Majik.Core.Abilities.Triggers.OnEnterBattlefieldSelf(permanent),
            effects: System.Array.Empty<Majik.Core.Abilities.IEffect>());
        _bus.Publish(new StackObjectResolvedEvent(trig));

        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().Be(0,
            "a dead window auto-passes immediately after a resolution too — " +
            "no server-side display beat to block on");
    }

    // Integration-shape regression: the EXACT replay-confirmed wedge. An
    // OPPONENT-controlled spell sits on top of the stack; the human (Alice)
    // gets a DEAD priority window on it (her only legal move is pass), and a
    // CountingAgent stands in for the RemoteAgent human that NEVER submits
    // anything. Server-side auto-pass alone must carry the dead window: the
    // loop synthesizes Alice's pass WITHOUT ever invoking her agent, the
    // opponent's spell RESOLVES, the stack DRAINS, and the round ENDS — within
    // a bounded timeout, never blocking forever. Before the fix the freshly
    // PUSHED stack object sat inside the display beat → auto-pass suppressed →
    // the loop awaited Alice's pass that never came → permanent wedge.
    [Fact]
    public async Task OpponentSpellOnStack_HumanNeverSubmits_DeadWindowAutoPasses_GameAdvances()
    {
        // Alice = the "human" seat. Her agent would BLOCK forever if prompted
        // (a never-submitting RemoteAgent analogue): ChoosePriorityActionAsync
        // here returns Pass, but the WEDGE shape is that it is reached at all —
        // we assert PromptCount stays 0, i.e. the agent is never consulted.
        var alice = new CountingAgent();
        var bob = new CountingAgent();

        // Every window in this scenario is pass-only from the holder's POV:
        // Alice (human, only lands) can never respond to Bob's spell, and once
        // it resolves the empty-stack windows are dead too. Bob's own window
        // (his spell on top) auto-passes via the own-top reason regardless.
        // prefs: non-null for Alice (the human seat → eligible for auto-pass),
        // null for Bob (bot seat → drives himself; here he's a CountingAgent
        // that just passes).
        var loop = NewLoop(alice, bob,
            prefs: p => ReferenceEquals(p, _alice) ? new TestPrefs() : null,
            deadWindow: _ => true,
            phase: StepStateType.PreCombatMain);

        // BOB (the bot/opponent) controls the top of the stack — his spell.
        PushOwnedStackObject(_bob);

        // Bound the whole thing so a regression manifests as a FAILED test, not
        // a hung suite. The fixed loop drains synchronously in microseconds.
        var run = loop.RunUntilRoundEndsAsync(_alice);
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)));

        finished.Should().BeSameAs(run,
            "the dead window must auto-pass server-side and let the opponent's " +
            "spell resolve — never block awaiting the human forever (the wedge)");
        await run; // surface any exception

        alice.PromptCount.Should().Be(0,
            "Alice's dead window was auto-passed server-side; her agent was never " +
            "consulted, so a never-submitting human cannot wedge the clock");
        _stack.IsEmpty.Should().BeTrue("the opponent's spell resolved and the stack drained");
    }

    // -------------------------------------------------------------------
    // Own-top-of-stack auto-pass — the player who controls the top object
    // of the stack (their own spell / activated ability / trigger) should
    // auto-pass on it by default (CR 117 "don't respond to your own
    // object"), even when the window is NOT dead (e.g. an untapped land
    // keeps a mana ability legal). Only Full Control / a phase stop
    // surfaces the prompt.
    // -------------------------------------------------------------------

    /// <summary>
    /// Push a no-op object the given player controls onto the stack so
    /// <c>ctx.Stack.Top.Controller</c> resolves to that player.
    /// </summary>
    private void PushOwnedStackObject(Player controller)
    {
        var permanent = new Majik.Core.Cards.Creature("Goblin", "R", 1, 1)
        {
            Owner = controller,
            Zone = ZoneType.Battlefield,
        };
        var trig = new Majik.Core.Abilities.TriggeredAbility(
            permanent, controller,
            Majik.Core.Abilities.Triggers.OnEnterBattlefieldSelf(permanent),
            effects: System.Array.Empty<Majik.Core.Abilities.IEffect>());
        _stack.Push(trig);
    }

    [Fact]
    public async Task OwnTopOfStack_DefaultPrefs_AutoPasses_EvenWhenWindowNotDead()
    {
        // Alice controls the top of the stack (she just cast/activated it).
        // The window is NOT dead (deadWindow only fires on an empty stack —
        // mirrors an untapped land keeping a mana ability legal). She must
        // still auto-pass her own object; Bob (not the controller) is
        // prompted so he can respond.
        var alice = new CountingAgent();
        var bob = new CountingAgent();
        var loop = NewLoop(alice, bob,
            prefs: _ => new TestPrefs(),
            deadWindow: ctx => ctx.Stack.IsEmpty);

        PushOwnedStackObject(_alice);

        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().Be(0,
            "Alice controls the top of stack → auto-pass her own object despite the non-dead window");
        bob.PromptCount.Should().Be(1,
            "Bob does not control the top object → he is prompted so he can respond, then passes");
        _stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task OwnTopOfStack_FullControl_PromptsAgent()
    {
        // Full Control means the player WANTS to respond to their own
        // spells/effects → never auto-pass the own-top window.
        var alice = new CountingAgent();
        var bob = new CountingAgent();
        var loop = NewLoop(alice, bob,
            prefs: _ => new TestPrefs { FullControl = true },
            deadWindow: ctx => ctx.Stack.IsEmpty);

        PushOwnedStackObject(_alice);

        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().BeGreaterThan(0,
            "Full Control surfaces the prompt on Alice's own stack object");
    }

    [Fact]
    public async Task OpponentTopOfStack_DoesNotAutoPassViaOwnPath()
    {
        // The top object is controlled by Bob; Alice does NOT control it,
        // so the own-top path must not fire for her — she is prompted so
        // she can respond to the opponent's spell/effect.
        var alice = new CountingAgent();
        var bob = new CountingAgent();
        var loop = NewLoop(alice, bob,
            prefs: _ => new TestPrefs(),
            deadWindow: ctx => ctx.Stack.IsEmpty);

        PushOwnedStackObject(_bob);

        await loop.RunUntilRoundEndsAsync(_alice);

        alice.PromptCount.Should().Be(1,
            "the top object is the opponent's → Alice is prompted so she can respond");
        bob.PromptCount.Should().Be(0,
            "Bob controls the top object → he auto-passes his own object");
    }

    [Fact]
    public async Task OwnTopOfStack_AutoPasses_NonDeadWindow_FreshMutation()
    {
        // Own-top auto-pass fires even when the window is NOT dead (an untapped
        // land keeps a mana ability legal) and even immediately after the
        // mutation that put the object on the stack — there is no server-side
        // display beat to suppress it. (Previously this asserted the own-top
        // exemption from the now-removed Gate 5; the behaviour is unchanged.)
        var alice = new CountingAgent();
        var bob = new CountingAgent();

        var loop = NewLoop(alice, bob,
            prefs: _ => new TestPrefs(),
            deadWindow: _ => false, // never a dead window → own-top is the only auto-pass reason
            phase: StepStateType.PreCombatMain);

        PushOwnedStackObject(_alice);

        await loop.RunUntilRoundEndsAsync(_alice);

        // Alice auto-passes her own object in round 1 and is prompted only in
        // the trailing empty-stack round (count 1).
        alice.PromptCount.Should().Be(1,
            "Alice auto-passes her own top-of-stack object immediately, no display beat");
    }
}
