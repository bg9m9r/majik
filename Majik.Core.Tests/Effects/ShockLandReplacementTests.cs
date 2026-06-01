using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Land = Majik.Core.Cards.Land;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 614.1c — production binder-chain replacement. Mirrors the test-only
/// <c>ShockLandCycleFactory</c> shape: when an agent is registered for the
/// land's controller the replacement consults <c>ChooseYesNoAsync</c>; with
/// no agent it preserves the legacy "pay-if-life&gt;2, else tapped" fallback
/// so pre-agent integration tests / no-agent paths don't regress.
/// </summary>
public class ShockLandReplacementTests : IDisposable
{
    public ShockLandReplacementTests()
    {
        // Tests register agents into the global AgentRegistry — clear in
        // ctor + Dispose so we never inherit a stale registration from
        // a neighbour test in the same class.
        AgentRegistry.Clear();
    }

    public void Dispose() => AgentRegistry.Clear();

    private static (Player alice, Land land, ReplacementBus bus) MakeWorld(int life = 20)
    {
        var alice = new Player("Alice", life);
        var land = new Land("Overgrown Tomb") { Owner = alice, Zone = ZoneType.Hand };
        var bus = new ReplacementBus();
        bus.Register(new ShockLandReplacement(land));
        return (alice, land, bus);
    }

    private static ZoneMoveIntent EtbIntent(Land land, Player controller) =>
        new(land, ZoneType.Hand, ZoneType.Battlefield, Controller: controller);

    // -----------------------------------------------------------------
    // No-agent fallback (preserves legacy ShockLandBinderTests posture)
    // -----------------------------------------------------------------

    [Fact]
    public void NoAgent_HighLife_AutoPays2Life_EntersUntapped()
    {
        var (alice, land, bus) = MakeWorld(life: 20);
        // No AgentRegistry.Set — explicit no-agent fallback.

        var after = bus.Apply(EtbIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse();
        alice.LifeTotal.Should().Be(18,
            "no-agent fallback preserves legacy auto-pay-2-life posture");
    }

    [Fact]
    public void NoAgent_LowLife_EntersTapped_NoLifePaid()
    {
        var (alice, land, bus) = MakeWorld(life: 2);
        // No AgentRegistry.Set.

        var after = bus.Apply(EtbIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue();
        alice.LifeTotal.Should().Be(2, "CR 119.4 — refuse the auto-suicide");
    }

    // -----------------------------------------------------------------
    // Sync no-prompt posture (ReplacementBus.Apply) — the no-context path
    // does NOT prompt (CR 614 choices must be awaited, never bridged
    // sync-over-async); it applies the deterministic auto-pay posture.
    // -----------------------------------------------------------------

    [Fact]
    public void SyncApply_DoesNotPrompt_AutoPaysWhenLifeToSpare()
    {
        var (alice, land, bus) = MakeWorld(life: 20);
        // Even with an agent registered, the SYNC path never prompts — it
        // auto-pays. (A throwing agent would surface if it were consulted.)
        var agent = new ScriptedAgent(); // empty queue: Pop throws if prompted
        AgentRegistry.Set(alice, agent);

        var after = bus.Apply(EtbIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse("sync path auto-pays when life > 2");
        alice.LifeTotal.Should().Be(18, "deterministic auto-pay-2-life posture");
    }

    // -----------------------------------------------------------------
    // Async agent-driven prompt (ReplacementBus.ApplyAsync) — production path.
    // -----------------------------------------------------------------

    [Fact]
    public async Task AgentSaysYes_HighLife_Pays2Life_EntersUntapped()
    {
        var (alice, land, bus) = MakeWorld(life: 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        var ctx = ResolutionContext.For(alice, agent, game: null, chosenTargets: null);

        var after = await bus.ApplyAsync(EtbIntent(land, alice), ctx);

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "agent answered yes → land enters untapped");
        alice.LifeTotal.Should().Be(18, "yes path debits 2 life");
    }

    [Fact]
    public async Task AgentSaysNo_HighLife_EntersTapped_NoLifePaid()
    {
        var (alice, land, bus) = MakeWorld(life: 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        var ctx = ResolutionContext.For(alice, agent, game: null, chosenTargets: null);

        var after = await bus.ApplyAsync(EtbIntent(land, alice), ctx);

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "agent declined → land enters tapped");
        alice.LifeTotal.Should().Be(20, "no payment when declined");
    }

    [Fact]
    public async Task AgentRegistered_LifeTwoOrLess_NoPromptFired_EntersTapped()
    {
        // Per spec deferral: at LifeTotal <= 2 the production replacement
        // skips the prompt entirely and enters tapped. ScriptedAgent
        // would throw if prompted (empty yes/no queue) — surviving means
        // no prompt fired. CR 119.4 conservative posture.
        var (alice, land, bus) = MakeWorld(life: 2);
        var agent = new ScriptedAgent();
        // No QueueYesNo.
        var ctx = ResolutionContext.For(alice, agent, game: null, chosenTargets: null);

        var after = await bus.ApplyAsync(EtbIntent(land, alice), ctx);

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue();
        alice.LifeTotal.Should().Be(2, "no prompt → no payment");
    }

    // -----------------------------------------------------------------
    // PLAN 08 — async replacement path (ReplacementBus.ApplyAsync). The
    // production cast-resolution path awaits the controller's agent off the
    // ResolutionContext rather than blocking a thread on a sync-over-async
    // bridge.
    // -----------------------------------------------------------------

    [Fact]
    public async Task ApplyAsync_AgentSaysYes_Pays2Life_EntersUntapped()
    {
        var (alice, land, bus) = MakeWorld(life: 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        var ctx = ResolutionContext.For(alice, agent, game: null, chosenTargets: null);

        var after = await bus.ApplyAsync(EtbIntent(land, alice), ctx);

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse("agent answered yes → untapped");
        alice.LifeTotal.Should().Be(18, "yes path debits 2 life");
    }

    [Fact]
    public async Task ApplyAsync_AgentSaysNo_EntersTapped_NoLifePaid()
    {
        var (alice, land, bus) = MakeWorld(life: 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        var ctx = ResolutionContext.For(alice, agent, game: null, chosenTargets: null);

        var after = await bus.ApplyAsync(EtbIntent(land, alice), ctx);

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue("agent declined → tapped");
        alice.LifeTotal.Should().Be(20, "no payment when declined");
    }

    [Fact]
    public async Task ApplyAsync_HumanThinkTime_IsGenuinelyAwaited_NoSyncBridge()
    {
        // A "human" agent whose ChooseYesNoAsync parks on an un-signalled
        // TaskCompletionSource — modelling real think-time. The async bus must
        // genuinely await it (a sync-over-async bridge would block the thread
        // forever). Once the human answers, the choice is honoured.
        var (alice, land, bus) = MakeWorld(life: 20);
        var human = new DeferredYesNoAgent();
        var ctx = ResolutionContext.For(alice, human, game: null, chosenTargets: null);

        var applyTask = bus.ApplyAsync(EtbIntent(land, alice), ctx);

        human.WasPrompted.Should().BeTrue("the replacement awaited the agent");
        applyTask.IsCompleted.Should().BeFalse(
            "the human has not answered yet — the bus must not have bridged sync-over-async");
        alice.LifeTotal.Should().Be(20, "no life debited until the human answers");

        human.Answer(true); // human chooses to pay
        var after = await applyTask;

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse("human paid → untapped");
        alice.LifeTotal.Should().Be(18, "human's yes debited 2 life after the await resumed");
    }

    /// <summary>
    /// Test agent whose <see cref="ChooseYesNoAsync(string,BotIntent,CancellationToken)"/>
    /// returns a task that only completes when <see cref="Answer"/> is called —
    /// modelling a human's think-time. Used to prove the replacement genuinely
    /// awaits the agent (no sync-over-async bridge).
    /// </summary>
    private sealed class DeferredYesNoAgent : IPlayerAgent
    {
        private readonly TaskCompletionSource<bool> _yesNo =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasPrompted { get; private set; }
        public void Answer(bool yes) => _yesNo.SetResult(yes);

        public Task<bool> ChooseYesNoAsync(
            GameContext? ctx, string question, string? sourceCardName, CancellationToken ct = default)
        {
            WasPrompted = true;
            return _yesNo.Task;
        }

        public Task<bool> ChooseYesNoAsync(string question, BotIntent intent, CancellationToken ct = default)
        {
            WasPrompted = true;
            return _yesNo.Task;
        }

        // Remaining IPlayerAgent surface is unused by the shock-land prompt.
        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
