using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Simulation;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Simulation;

/// <summary>
/// Combat-state resume seam (root block search): a sandbox cloned from a
/// mid-combat live state + a <see cref="CombatResumeState"/> must reach the
/// defender's block decision with the REAL declared attackers (InstanceId
/// match) and must NOT re-run the declaration half (CR 508.1f events fired
/// live, before the clone).
/// </summary>
public sealed class CombatStateResumeTests
{
    [Fact]
    public async Task ResumeIntoBlocks_ReachesDefenderWithRealAttack_NoRedeclaration()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Live pre-state: attack already DECLARED (attacker tapped, CR 508.1f
        // events fired in the live game before the clone).
        var liveAttacker = new Creature("Grizzly Bears", "1G", 2, 2);
        liveAttacker.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(liveAttacker);
        liveAttacker.SetZone(ZoneType.Battlefield);
        liveAttacker.HasSummoningSickness = false;
        liveAttacker.Tap();

        // An untapped potential blocker on the defending side.
        var liveBlocker = new Creature("Eager First-Year", "1W", 1, 2);
        liveBlocker.ChangeOwner(bob);
        bob.Zones.Battlefield.AddCard(liveBlocker);
        liveBlocker.SetZone(ZoneType.Battlefield);

        IReadOnlyList<Creature>? capturedAttackers = null;
        var recordingAgent = new RecordingDefenderAgent(
            attackers => capturedAttackers = attackers);

        var sandbox = SandboxGame.From(
            new[] { alice, bob },
            rng: new GameRandom(1),
            agentFactory: p => p.Id == bob.Id
                ? recordingAgent
                : new DeterministicBotAgent());

        var attacksFired = 0;
        sandbox.Bus.Subscribe<Majik.Core.Domain.DomainEvents.CreatureAttacksEvent>(
            _ => attacksFired++);

        var resume = CombatResumeState.FromAttackers(new[] { liveAttacker }, bob);
        var clonedActive = sandbox.State.PlayerFor(alice);

        await sandbox.ResumeAsync(
            PhaseStateType.Combat, clonedActive, turnNumber: 4, maxTurns: 4,
            combatResume: resume);

        capturedAttackers.Should().NotBeNull("the defender's block ask must be reached");
        capturedAttackers!.Select(a => a.InstanceId)
            .Should().Equal(liveAttacker.InstanceId);
        attacksFired.Should().Be(0,
            "the declaration half ran live before the clone (CR 508.1f must not double-fire)");
    }

    [Fact]
    public async Task ResumeIntoBlocks_NoSurvivingAttacker_CombatFizzles_GameContinues()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // The "declared" attacker is NOT placed on any battlefield, so its
        // InstanceId cannot resolve in the clone — the rebound plan is null
        // and the resumed combat fizzles without throwing.
        var ghost = new Creature("Grizzly Bears", "1G", 2, 2);
        ghost.ChangeOwner(alice);

        var sandbox = SandboxGame.From(
            new[] { alice, bob },
            rng: new GameRandom(1),
            agentFactory: _ => new DeterministicBotAgent());

        var resume = CombatResumeState.FromAttackers(new[] { ghost }, bob);

        var result = await sandbox.ResumeAsync(
            PhaseStateType.Combat, sandbox.State.PlayerFor(alice),
            turnNumber: 4, maxTurns: 4, combatResume: resume);

        result.Should().NotBeNull();
        sandbox.State.PlayerFor(bob).LifeTotal.Should().Be(20);
    }

    /// <summary>
    /// Pass-everything agent whose <see cref="DeclareBlockersAsync"/> records
    /// the attackers it was shown and blocks nothing.
    /// </summary>
    private sealed class RecordingDefenderAgent : IPlayerAgent
    {
        private readonly Action<IReadOnlyList<Creature>> _onBlockAsk;

        public RecordingDefenderAgent(Action<IReadOnlyList<Creature>> onBlockAsk)
            => _onBlockAsk = onBlockAsk;

        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
        {
            _onBlockAsk(attackers);
            return Task.FromResult(BlockPlan.None);
        }

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => Task.FromResult(PriorityAction.Pass);

        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => Task.FromResult(MulliganDecision.Keep);

        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ICard>>(hand.Take(countToBottom).ToList());

        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<object>>(request.LegalCandidates.Take(request.MinTargets).ToList());

        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ITriggeredAbility>>(mine.ToList());

        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
            => Task.FromResult(ManaPayment.Empty);

        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
            => Task.FromResult(CombatPlan.None);

        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new ScryAction.ScryDecision(ToBottom: peeked.ToList(), TopOrder: Array.Empty<ICard>()));

        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new SurveilAction.SurveilDecision(ToGraveyard: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
    }
}
