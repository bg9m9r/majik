using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

public class LoyaltyAbilityTests
{
    private readonly Player _alice = new("Alice", 20);

    private Planeswalker MakeWalker(int loyalty = 4)
    {
        var pw = new Planeswalker("Jace", "2UU", loyalty)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        return pw;
    }

    [Fact]
    public void PlusOne_AddsLoyalty()
    {
        var pw = MakeWalker(4);
        var plusOne = new LoyaltyAbility(pw, +1, () => { });
        plusOne.CanActivate().Should().BeTrue();
        plusOne.Activate();
        pw.Loyalty.Should().Be(5);
    }

    [Fact]
    public void MinusTwo_RemovesLoyalty_AndExecutesEffect()
    {
        var pw = MakeWalker(4);
        var effectRan = false;
        var minusTwo = new LoyaltyAbility(pw, -2, () => effectRan = true);

        minusTwo.CanActivate().Should().BeTrue();
        minusTwo.Activate();

        pw.Loyalty.Should().Be(2);
        effectRan.Should().BeTrue();
    }

    [Fact]
    public void Ultimate_CannotActivate_WhenLoyaltyTooLow()
    {
        var pw = MakeWalker(3);
        var ult = new LoyaltyAbility(pw, -7, () => { });
        ult.CanActivate().Should().BeFalse();
    }

    [Fact]
    public void OncePerTurn_SecondActivationBlocked()
    {
        var pw = MakeWalker(5);
        var first = new LoyaltyAbility(pw, +1, () => { });
        var second = new LoyaltyAbility(pw, -1, () => { });

        first.CanActivate().Should().BeTrue();
        first.Activate();

        // Same planeswalker, second loyalty ability — blocked.
        second.CanActivate().Should().BeFalse();
    }

    [Fact]
    public void OncePerTurn_ResetsAtTurnEnd()
    {
        var pw = MakeWalker(5);
        var first = new LoyaltyAbility(pw, +1, () => { });
        first.Activate();
        pw.ResetTurnState();

        var second = new LoyaltyAbility(pw, -1, () => { });
        second.CanActivate().Should().BeTrue();
    }
}
