using FluentAssertions;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Xunit;

public class LandDropTrackerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void DefaultMax_OnePerTurn()
    {
        var t = new LandDropTracker();
        t.CanPlayLand(_alice, _alice, PhaseStateType.PreCombatMain, true, out _).Should().BeTrue();
        t.RecordLandPlayed(_alice);
        t.CanPlayLand(_alice, _alice, PhaseStateType.PreCombatMain, true, out var reason).Should().BeFalse();
        reason.Should().Contain("already played");
    }

    [Fact]
    public void OnOpponentTurn_Rejected()
    {
        var t = new LandDropTracker();
        t.CanPlayLand(_alice, _bob, PhaseStateType.PreCombatMain, true, out var reason).Should().BeFalse();
        reason.Should().Contain("your turn");
    }

    [Fact]
    public void OutsideMain_Rejected()
    {
        var t = new LandDropTracker();
        t.CanPlayLand(_alice, _alice, PhaseStateType.End, true, out var reason).Should().BeFalse();
        reason.Should().Contain("main phase");
    }

    [Fact]
    public void StackNotEmpty_Rejected()
    {
        var t = new LandDropTracker();
        t.CanPlayLand(_alice, _alice, PhaseStateType.PreCombatMain, false, out var reason).Should().BeFalse();
        reason.Should().Contain("stack is empty");
    }

    [Fact]
    public void ExtraLandDrops_Honored()
    {
        var t = new LandDropTracker();
        t.SetMaxLandDropsThisTurn(_alice, 3);

        for (var i = 0; i < 3; i++)
        {
            t.CanPlayLand(_alice, _alice, PhaseStateType.PreCombatMain, true, out _).Should().BeTrue();
            t.RecordLandPlayed(_alice);
        }
        t.CanPlayLand(_alice, _alice, PhaseStateType.PreCombatMain, true, out _).Should().BeFalse();
    }

    [Fact]
    public void ResetTurn_ClearsCount_AndResetMax()
    {
        var t = new LandDropTracker();
        t.SetMaxLandDropsThisTurn(_alice, 3);
        t.RecordLandPlayed(_alice);
        t.RecordLandPlayed(_alice);

        t.ResetTurn();

        t.DropsUsedThisTurn(_alice).Should().Be(0);
        t.MaxLandDropsThisTurn(_alice).Should().Be(1);
    }
}
