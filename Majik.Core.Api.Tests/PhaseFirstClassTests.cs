using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.StateMachine;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Slice 3 — Pre/PostCombatMain first-class. The engine's
/// <see cref="StepStateType"/> value is now the authoritative distinct
/// label; <see cref="PhaseLabelResolver"/> no longer reconstructs the
/// pre/post distinction from <see cref="PhaseStateType"/> (CR 505).
/// </summary>
public class PhaseFirstClassTests
{
    [Fact]
    public void Resolve_PreCombatMain_IsDistinct_WithoutTurnState()
    {
        PhaseLabelResolver.Resolve(StepStateType.PreCombatMain, null)
            .Should().Be("PreCombatMain");
    }

    [Fact]
    public void Resolve_PostCombatMain_IsDistinct_WithoutTurnState()
    {
        PhaseLabelResolver.Resolve(StepStateType.PostCombatMain, null)
            .Should().Be("PostCombatMain");
    }

    [Theory]
    [InlineData(StepStateType.PreCombatMain, true)]
    [InlineData(StepStateType.PostCombatMain, true)]
    [InlineData(StepStateType.Upkeep, false)]
    [InlineData(StepStateType.BeginningOfCombat, false)]
    public void IsMain_TrueOnlyForTheTwoMainPhases(StepStateType phase, bool expected)
    {
        phase.IsMain().Should().Be(expected);
    }
}
