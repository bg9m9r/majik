using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>Unit tests for <see cref="BirdsOfParadiseFactory"/>.</summary>
public class BirdsOfParadiseTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BirdsOfParadise_NameIsCorrect()
    {
        var bop = BirdsOfParadiseFactory.Create(_alice);
        bop.Name.Should().Be("Birds of Paradise");
    }

    [Fact]
    public void BirdsOfParadise_IsCreature()
    {
        var bop = BirdsOfParadiseFactory.Create(_alice);
        bop.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void BirdsOfParadise_HasBirdSubtype()
    {
        var bop = BirdsOfParadiseFactory.Create(_alice);
        bop.HasSubtype(CardSubtype.Bird).Should().BeTrue("Birds of Paradise is a Bird");
    }

    [Fact]
    public void BirdsOfParadise_PowerAndToughnessAreZeroOne()
    {
        var bop = BirdsOfParadiseFactory.Create(_alice);
        bop.Power.Should().Be(0);
        bop.Toughness.Should().Be(1);
    }

    [Fact]
    public void BirdsOfParadise_ManaCostIsGreen()
    {
        var bop = BirdsOfParadiseFactory.Create(_alice);
        bop.ManaCost.Should().Be("{G}");
    }

    [Fact]
    public void BirdsOfParadise_OwnerAndControllerAreSet()
    {
        var bop = BirdsOfParadiseFactory.Create(_alice);
        bop.Owner.Should().BeSameAs(_alice);
        bop.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BirdsOfParadise_HasFlyingKeyword()
    {
        var bop = BirdsOfParadiseFactory.Create(_alice);
        bop.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "Birds of Paradise has the printed Flying ability (CR 702.9)");
    }

    [Fact]
    public void BirdsOfParadise_HasFiveManaAbilities_OnePerColor()
    {
        var bop = BirdsOfParadiseFactory.Create(_alice);
        var mas = bop.Abilities.OfType<ManaAbility>().ToList();
        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour");
    }
}
