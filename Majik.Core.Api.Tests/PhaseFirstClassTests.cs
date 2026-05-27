using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.StateMachine;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Slice 3 — Pre/PostCombatMain first-class. The engine's
/// <see cref="PhaseStateType"/> value is now the authoritative distinct
/// label; <see cref="PhaseLabelResolver"/> no longer reconstructs the
/// pre/post distinction from <see cref="TurnStateType"/> (CR 505).
/// </summary>
public class PhaseFirstClassTests
{
    [Fact]
    public void Resolve_PreCombatMain_IsDistinct_WithoutTurnState()
    {
        PhaseLabelResolver.Resolve(PhaseStateType.PreCombatMain, null)
            .Should().Be("PreCombatMain");
    }

    [Fact]
    public void Resolve_PostCombatMain_IsDistinct_WithoutTurnState()
    {
        PhaseLabelResolver.Resolve(PhaseStateType.PostCombatMain, null)
            .Should().Be("PostCombatMain");
    }

    [Theory]
    [InlineData(PhaseStateType.PreCombatMain, true)]
    [InlineData(PhaseStateType.PostCombatMain, true)]
    [InlineData(PhaseStateType.Upkeep, false)]
    [InlineData(PhaseStateType.BeginningOfCombat, false)]
    public void IsMain_TrueOnlyForTheTwoMainPhases(PhaseStateType phase, bool expected)
    {
        phase.IsMain().Should().Be(expected);
    }
}
