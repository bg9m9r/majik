using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Xunit;

public class CastingPermissionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Instant_CastableAnytime()
    {
        var bolt = new Instant("Bolt", "R");

        CastingPermission.CanCast(bolt, _alice, _bob,
            PhaseStateType.End, stackIsEmpty: false, out _).Should().BeTrue();
    }

    [Fact]
    public void Sorcery_OnOwnTurn_MainPhase_EmptyStack_OK()
    {
        var sorc = new Sorcery("Divination", "2U");

        CastingPermission.CanCast(sorc, _alice, _alice,
            PhaseStateType.PreCombatMain, stackIsEmpty: true, out _).Should().BeTrue();
    }

    [Fact]
    public void Sorcery_OnOpponentTurn_Rejected()
    {
        var sorc = new Sorcery("Divination", "2U");

        CastingPermission.CanCast(sorc, _alice, _bob,
            PhaseStateType.PreCombatMain, stackIsEmpty: true, out var reason).Should().BeFalse();
        reason.Should().Contain("your turn");
    }

    [Fact]
    public void Sorcery_OutsideMain_Rejected()
    {
        var sorc = new Sorcery("Divination", "2U");

        CastingPermission.CanCast(sorc, _alice, _alice,
            PhaseStateType.End, stackIsEmpty: true, out var reason).Should().BeFalse();
        reason.Should().Contain("main phase");
    }

    [Fact]
    public void Sorcery_StackNotEmpty_Rejected()
    {
        var sorc = new Sorcery("Divination", "2U");

        CastingPermission.CanCast(sorc, _alice, _alice,
            PhaseStateType.PreCombatMain, stackIsEmpty: false, out var reason).Should().BeFalse();
        reason.Should().Contain("stack is empty");
    }

    [Fact]
    public void Land_AlwaysRejected_NotASpell()
    {
        var land = new Land("Mountain");

        CastingPermission.CanCast(land, _alice, _alice,
            PhaseStateType.PreCombatMain, stackIsEmpty: true, out var reason).Should().BeFalse();
        reason.Should().Contain("LandDropTracker");
    }
}
