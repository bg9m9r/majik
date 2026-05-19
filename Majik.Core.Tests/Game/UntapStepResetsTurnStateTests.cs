using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class UntapStepResetsTurnStateTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ResetTurnState_ClearsLoyaltyFlagAndSummoningSickness()
    {
        var pw = new Planeswalker("Jace", "2UU", 4)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        new LoyaltyAbility(pw, +1, () => { }).Activate();
        pw.HasSummoningSickness = true;
        pw.LoyaltyAbilityActivatedThisTurn.Should().BeTrue();

        pw.ResetTurnState();

        pw.LoyaltyAbilityActivatedThisTurn.Should().BeFalse();
        pw.HasSummoningSickness.Should().BeFalse();
    }

    [Fact]
    public void ResetTurnState_CallableOnAnyPermanent()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        bear.HasSummoningSickness = true;
        bear.ResetTurnState();
        bear.HasSummoningSickness.Should().BeFalse();
    }
}
