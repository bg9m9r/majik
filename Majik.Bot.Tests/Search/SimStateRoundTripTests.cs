using FluentAssertions;
using Majik.Bot.Search;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Round-trip guard for SimState copy helpers — the fetchland copy-ctor bug
/// class: a copy helper that silently drops a field is invisible until a
/// live game hits the dropped path. Every copy helper must preserve
/// <see cref="SimState.PreDeclaredAttack"/>.
/// </summary>
public class SimStateRoundTripTests
{
    [Fact]
    public void CopyHelpers_PreservePreDeclaredAttack()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var players = new[] { alice, bob };
        var resume = new CombatResumeState(new[] { Guid.NewGuid() }, Guid.NewGuid());

        var root = SimState.Capture(
            players, alice, 5, PhaseStateType.Combat, bob,
            preDeclaredAttack: resume);

        root.PreDeclaredAttack.Should().BeSameAs(resume);
        root.WithDeterminization(new[] { "Forest" }, 42)
            .PreDeclaredAttack.Should().BeSameAs(resume);
        root.WithDeterminization(new[] { "Forest" }, observedPublic: new[] { "Plains" }, 42)
            .PreDeclaredAttack.Should().BeSameAs(resume);
        root.WithDeterminization(new[] { "Forest" }, 42).WithWorldSeed(7)
            .PreDeclaredAttack.Should().BeSameAs(resume);
    }

    [Fact]
    public void Capture_DefaultsPreDeclaredAttack_ToNull()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var root = SimState.Capture(
            new[] { alice, bob }, alice, 5, PhaseStateType.Combat, bob);

        root.PreDeclaredAttack.Should().BeNull();
    }
}
