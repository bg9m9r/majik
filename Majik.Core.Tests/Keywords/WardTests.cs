using FluentAssertions;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class WardTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void OpponentCasts_DidNotPay_Counters()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        var ward = new WardEffect(bear, ManaCost.Parse("2"));

        ward.ResolvesWard(_alice, casterPaidWardCost: false).Should().BeTrue();
    }

    [Fact]
    public void OpponentCasts_Paid_NoCounter()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        var ward = new WardEffect(bear, ManaCost.Parse("2"));

        ward.ResolvesWard(_alice, casterPaidWardCost: true).Should().BeFalse();
    }

    [Fact]
    public void OwnControllerTargets_DoesNotTrigger()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        var ward = new WardEffect(bear, ManaCost.Parse("2"));

        ward.ResolvesWard(_bob, casterPaidWardCost: false).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // CR 702.21c — non-mana Ward (discard / pay-life / sacrifice).
    // Resolve() charges the ICost and returns whether the targeting
    // spell/ability is countered.
    // ------------------------------------------------------------------

    [Fact]
    public void NonManaWard_Discard_OpponentHasCard_PaysAndIsNotCountered()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        var ward = new WardEffect(bear, new DiscardACardCost());

        // Alice (the opponent targeting Bob's Bear) has a card to discard.
        var spare = new Creature("Spare", "{1}", 1, 1) { Owner = _alice, Controller = _alice };
        _alice.Zones.Hand.AddCard(spare);

        var countered = ward.Resolve(_alice);

        countered.Should().BeFalse("Alice can and does pay the discard ward cost");
        _alice.Zones.Hand.GetCards().Should().NotContain(spare,
            "the ward discard moved Spare from hand to graveyard");
        _alice.Zones.Graveyard.GetCards().Should().Contain(spare);
    }

    [Fact]
    public void NonManaWard_Discard_OpponentHasNoCard_IsCountered()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        var ward = new WardEffect(bear, new DiscardACardCost());

        // Alice has an empty hand — can't pay the discard ward → countered.
        var countered = ward.Resolve(_alice);

        countered.Should().BeTrue("Alice cannot pay the discard ward, so her spell is countered");
    }

    [Fact]
    public void NonManaWard_PayLife_OpponentPaysLife_NotCountered()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        var ward = new WardEffect(bear, new PayLifeCost(7));

        var countered = ward.Resolve(_alice);

        countered.Should().BeFalse("Alice can pay 7 life from 20");
        _alice.LifeTotal.Should().Be(13, "Ward—Pay 7 life charged 7 life");
    }

    [Fact]
    public void NonManaWard_PayLife_OpponentCannotAffordLife_IsCountered()
    {
        var poorAlice = new Player("PoorAlice", 5);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        var ward = new WardEffect(bear, new PayLifeCost(7));

        var countered = ward.Resolve(poorAlice);

        countered.Should().BeTrue("PoorAlice has only 5 life and cannot pay 7");
        poorAlice.LifeTotal.Should().Be(5, "no life was paid when the ward was not satisfiable");
    }

    [Fact]
    public void NonManaWard_OwnControllerTargets_NeverApplies()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        var ward = new WardEffect(bear, new PayLifeCost(7));

        // Bob (the controller) targeting his own creature — Ward does not apply.
        var countered = ward.Resolve(_bob);

        countered.Should().BeFalse("Ward only triggers off opponents' spells/abilities (CR 702.21e)");
        _bob.LifeTotal.Should().Be(20, "no life paid — ward did not apply");
    }

    [Fact]
    public void NonManaWard_OpponentDeclinesToPay_IsCountered()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        var ward = new WardEffect(bear, new PayLifeCost(7));

        // payIfAble: false models declining to pay even when able.
        var countered = ward.Resolve(_alice, payIfAble: false);

        countered.Should().BeTrue("Alice declined to pay the ward, so her spell is countered");
        _alice.LifeTotal.Should().Be(20, "no life paid when declining");
    }
}
