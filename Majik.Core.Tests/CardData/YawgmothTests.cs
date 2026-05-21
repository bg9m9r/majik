using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>Unit tests for <see cref="YawgmothFactory"/>.</summary>
public class YawgmothTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity / types
    // -----------------------------------------------------------------------

    [Fact]
    public void Yawgmoth_IsLegendaryCreature()
    {
        var yawg = YawgmothFactory.Create(_alice);

        yawg.HasType(CardType.Creature).Should().BeTrue();
        yawg.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    [Fact]
    public void Yawgmoth_HasPhyrexianHumanClericSubtypes()
    {
        var yawg = YawgmothFactory.Create(_alice);

        yawg.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        yawg.HasSubtype(CardSubtype.Human).Should().BeTrue();
        yawg.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
    }

    [Fact]
    public void Yawgmoth_IsTwoFour()
    {
        var yawg = YawgmothFactory.Create(_alice);

        yawg.BasePower.Should().Be(2);
        yawg.BaseToughness.Should().Be(4);
    }

    [Fact]
    public void Yawgmoth_OwnerAndControllerAreSet()
    {
        var yawg = YawgmothFactory.Create(_alice);

        yawg.Owner.Should().BeSameAs(_alice);
        yawg.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability count / presence
    // -----------------------------------------------------------------------

    [Fact]
    public void Yawgmoth_HasExactlyOneActivatedAbility()
    {
        var yawg = YawgmothFactory.Create(_alice);

        yawg.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Costs
    // -----------------------------------------------------------------------

    [Fact]
    public void Yawgmoth_AbilityCosts_IncludePayOneLife()
    {
        var yawg = YawgmothFactory.Create(_alice);
        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();

        ab.Costs.OfType<AdditionalCost>()
            .Should().Contain(
                ac => ac.Description.Contains("1 life"),
                "the cost must include 'Pay 1 life'");
    }

    [Fact]
    public void Yawgmoth_AbilityCosts_IncludeSacrificeAnotherCreature()
    {
        var yawg = YawgmothFactory.Create(_alice);
        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();

        ab.Costs.OfType<SacrificeAnotherCreatureCost>()
            .Should().HaveCount(1, "the cost must include a SacrificeAnotherCreatureCost");
    }

    [Fact]
    public void Yawgmoth_SacrificeAnotherCreatureCost_CannotPayWhenNoOtherCreature()
    {
        var yawg = YawgmothFactory.Create(_alice);
        // Yawgmoth is NOT yet on battlefield; alice has no creatures.
        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();
        var cost = ab.Costs.OfType<SacrificeAnotherCreatureCost>().Single();

        cost.CanPay(_alice).Should().BeFalse("no other creature on the battlefield");
    }

    [Fact]
    public void Yawgmoth_SacrificeAnotherCreatureCost_CanPayWhenOtherCreaturePresent()
    {
        var yawg = YawgmothFactory.Create(_alice);
        var fodder = new Creature("Grizzly Bears", "1G", 2, 2);
        fodder.SetOwner(_alice);
        fodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(fodder);

        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();
        var cost = ab.Costs.OfType<SacrificeAnotherCreatureCost>().Single();

        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void Yawgmoth_SacrificeAnotherCreatureCost_PayMovesCreatureToGraveyard()
    {
        var yawg = YawgmothFactory.Create(_alice);
        var fodder = new Creature("Grizzly Bears", "1G", 2, 2);
        fodder.SetOwner(_alice);
        fodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(fodder);

        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();
        var cost = ab.Costs.OfType<SacrificeAnotherCreatureCost>().Single();
        cost.Pay(_alice);

        _alice.Zones.Battlefield.GetCards().Should().NotContain(fodder);
        _alice.Zones.Graveyard.GetCards().Should().Contain(fodder);
    }

    // -----------------------------------------------------------------------
    // Effects — opponent resolver path
    // -----------------------------------------------------------------------

    [Fact]
    public void Yawgmoth_Effect_OpponentLosesOneLife()
    {
        var bob = new Player("Bob", 20);
        var yawg = YawgmothFactory.Create(_alice, () => new[] { _alice, bob });

        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        bob.LifeTotal.Should().Be(19, "Bob should lose 1 life");
        _alice.LifeTotal.Should().Be(20, "Alice (controller) should not lose life from this effect");
    }

    [Fact]
    public void Yawgmoth_Effect_ControllerDrawsACard()
    {
        // Seed Alice's library with one card.
        var topCard = new Card("Dark Ritual", "{B}");
        topCard.SetOwner(_alice);
        topCard.SetController(_alice);
        _alice.Zones.Library.AddCard(topCard);

        var yawg = YawgmothFactory.Create(_alice, () => new[] { _alice });

        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "the drawn card should be in Alice's hand");
        _alice.Zones.Library.GetCards().Should().NotContain(topCard, "drawn card should no longer be in library");
    }

    [Fact]
    public void Yawgmoth_Effect_OpponentDiscardsFirstCard()
    {
        var bob = new Player("Bob", 20);
        var discardTarget = new Card("Lightning Bolt", "{R}");
        discardTarget.SetOwner(bob);
        discardTarget.SetController(bob);
        bob.Zones.Hand.AddCard(discardTarget);

        var yawg = YawgmothFactory.Create(_alice, () => new[] { _alice, bob });

        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        bob.Zones.Hand.GetCards().Should().NotContain(discardTarget, "discarded card should leave hand");
        bob.Zones.Graveyard.GetCards().Should().Contain(discardTarget, "discarded card goes to graveyard");
    }

    [Fact]
    public void Yawgmoth_Effect_NoOpWhenNoOpponentsResolver()
    {
        var yawg = YawgmothFactory.Create(_alice);  // null resolver

        // Should resolve without throwing; opponent effects simply do nothing.
        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();
        var act = () => ab.Resolve();

        act.Should().NotThrow();
    }

    [Fact]
    public void Yawgmoth_Effect_DrawMarksEmptyLibrary()
    {
        // Alice's library is empty — draw should flag TriedToDrawFromEmptyLibrary.
        var yawg = YawgmothFactory.Create(_alice, () => new[] { _alice });

        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "CR 120.3: attempting to draw from an empty library sets the flag");
    }
}
