using FluentAssertions;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class AttackRestrictionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void GhostlyPrison_BlocksAttackUntilPaid()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };

        var restriction = new PayPerAttackerRestriction(_bob, ManaCost.Parse("2"));
        var reg = new AttackRestrictionRegistry();
        reg.Register(restriction);

        reg.MayAttack(bear, _bob).Should().BeFalse();

        restriction.MarkPaid(bear);
        reg.MayAttack(bear, _bob).Should().BeTrue();
    }

    [Fact]
    public void GhostlyPrison_DoesNotAffectAttacksOnOtherPlayers()
    {
        var carl = new Player("Carl", 20);
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };

        var restriction = new PayPerAttackerRestriction(_bob, ManaCost.Parse("2"));
        var reg = new AttackRestrictionRegistry();
        reg.Register(restriction);

        reg.MayAttack(bear, carl).Should().BeTrue();
    }

    [Fact]
    public void ClearForTurn_ResetsPaidMarks()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };

        var restriction = new PayPerAttackerRestriction(_bob, ManaCost.Parse("2"));
        var reg = new AttackRestrictionRegistry();
        reg.Register(restriction);
        restriction.MarkPaid(bear);
        reg.MayAttack(bear, _bob).Should().BeTrue();

        restriction.ClearForTurn();
        reg.MayAttack(bear, _bob).Should().BeFalse();
    }
}
