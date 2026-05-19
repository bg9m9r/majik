using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class ProtectionBlockingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void AttackerProtectedFromRed_CannotBeBlockedByRedCreature()
    {
        var whiteKnight = new Creature("White Knight", "WW", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        whiteKnight.AddAbility(new ProtectionAbility("red"));

        var redBlocker = new Creature("Red Bear", "1R", 2, 2)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };

        var v = new CombatValidator();
        var attacker = new Attacker(whiteKnight, _bob);

        v.CanBlock(redBlocker, attacker, _bob).Should().BeFalse();
    }

    [Fact]
    public void AttackerProtectedFromRed_CanBeBlockedByGreenCreature()
    {
        var whiteKnight = new Creature("White Knight", "WW", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        whiteKnight.AddAbility(new ProtectionAbility("red"));

        var greenBlocker = new Creature("Green Bear", "1G", 2, 2)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };

        var v = new CombatValidator();
        var attacker = new Attacker(whiteKnight, _bob);
        v.CanBlock(greenBlocker, attacker, _bob).Should().BeTrue();
    }
}
