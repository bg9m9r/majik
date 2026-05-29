using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>Unit tests for <see cref="BirdsOfParadiseFactory"/>.</summary>
public class BirdsOfParadiseTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Power / toughness
    // -----------------------------------------------------------------------

    [Fact]
    public void BirdsOfParadise_ZeroOne()
    {
        var bop = BirdsOfParadiseFactory.Create(_alice);

        bop.Power.Should().Be(0);
        bop.Toughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — "{T}: Add one mana of any color." modeled as five
    // ManaAbility instances (one per WUBRG), mirroring Delighted Halfling.
    // -----------------------------------------------------------------------

    [Fact]
    public void BirdsOfParadise_HasFiveManaAbilities_OnePerColor()
    {
        var bop = BirdsOfParadiseFactory.Create(_alice);
        var mas = bop.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour");
    }

    // -----------------------------------------------------------------------
    // Flying (CR 702.9)
    // -----------------------------------------------------------------------

    [Fact]
    public void BirdsOfParadise_HasFlying()
    {
        var bop = BirdsOfParadiseFactory.Create(_alice);

        CombatAbilities.HasFlying(bop).Should().BeTrue("Birds of Paradise has Flying");
    }

    // -----------------------------------------------------------------------
    // Subtype
    // -----------------------------------------------------------------------

    [Fact]
    public void BirdsOfParadise_HasBirdSubtype()
    {
        var bop = BirdsOfParadiseFactory.Create(_alice);

        bop.HasSubtype(CardSubtype.Bird).Should().BeTrue("Birds of Paradise is a Bird");
    }

    // -----------------------------------------------------------------------
    // Owner / controller
    // -----------------------------------------------------------------------

    [Fact]
    public void BirdsOfParadise_OwnerAndControllerAreSet()
    {
        var bop = BirdsOfParadiseFactory.Create(_alice);

        bop.Owner.Should().BeSameAs(_alice);
        bop.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana cost
    // -----------------------------------------------------------------------

    [Fact]
    public void BirdsOfParadise_ManaCostIsGreen()
    {
        var bop = BirdsOfParadiseFactory.Create(_alice);

        bop.ManaCost.Should().Be("{G}");
    }
}
