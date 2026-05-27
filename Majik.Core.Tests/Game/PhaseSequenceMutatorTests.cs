using FluentAssertions;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.StateMachine;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>Locks the Phase-2-slice-3c extraction of CR 500.7–9
/// extra-phase queue logic into PhaseSequenceMutator.</summary>
public class PhaseSequenceMutatorTests
{
    [Fact]
    public void AddExtraCombatPhase_QueuesFiveCombatSteps()
    {
        var bus = new EventBus();
        var added = new List<PhaseStateType>();
        bus.Subscribe<ExtraPhaseAddedEvent>(e => added.Add(e.PhaseType));

        var mutator = new PhaseSequenceMutator(bus);
        mutator.AddExtraCombatPhase();

        mutator.PendingCount.Should().Be(5);
        added.Should().Equal(
            PhaseStateType.BeginningOfCombat,
            PhaseStateType.DeclareAttackers,
            PhaseStateType.DeclareBlockers,
            PhaseStateType.CombatDamage,
            PhaseStateType.EndOfCombat);
    }

    [Fact]
    public void TryDequeue_ReturnsFalseWhenEmpty()
    {
        var mutator = new PhaseSequenceMutator();
        mutator.TryDequeue(out _).Should().BeFalse();
    }

    [Fact]
    public void PeekAndDequeue_FollowFifoOrder()
    {
        var mutator = new PhaseSequenceMutator();
        mutator.AddExtraPhase(PhaseStateType.PreCombatMain);
        mutator.AddExtraPhase(PhaseStateType.End);

        mutator.PeekNext().Should().Be(PhaseStateType.PreCombatMain);
        mutator.TryDequeue(out var first).Should().BeTrue();
        first.Should().Be(PhaseStateType.PreCombatMain);
        mutator.TryDequeue(out var second).Should().BeTrue();
        second.Should().Be(PhaseStateType.End);
        mutator.PendingCount.Should().Be(0);
    }

    [Fact]
    public void Clear_DropsAllPending()
    {
        var mutator = new PhaseSequenceMutator();
        mutator.AddExtraMainPhase();
        mutator.AddExtraMainPhase();

        mutator.Clear();

        mutator.PendingCount.Should().Be(0);
    }
}
