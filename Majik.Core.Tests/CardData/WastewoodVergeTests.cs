using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="WastewoodVergeFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, non-legendary)
/// - Owner and controller assignment
/// - Two mana abilities: {G} and {B}
/// - Mana outputs are correct and exclusive
/// </summary>
public class WastewoodVergeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WastewoodVerge_IsLand()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void WastewoodVerge_NameIsCorrect()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.Name.Should().Be("Wastewood Verge");
    }

    [Fact]
    public void WastewoodVerge_IsNotLegendary()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void WastewoodVerge_OwnerAndControllerAreSet()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void WastewoodVerge_HasExactlyTwoManaAbilities()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {G} and one for {B}");
    }

    [Fact]
    public void WastewoodVerge_HasGreenManaAbility()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Black == 0,
                "must have exactly one {G} mana ability");
    }

    [Fact]
    public void WastewoodVerge_HasBlackManaAbility()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Green == 0,
                "must have exactly one {B} mana ability");
    }

    [Fact]
    public void WastewoodVerge_GreenManaAbility_ProducesOnlyGreen()
    {
        var land = WastewoodVergeFactory.Create(_alice);
        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.ManaGenerated.Generic.Should().Be(0);
        green.ManaGenerated.White.Should().Be(0);
        green.ManaGenerated.Blue.Should().Be(0);
        green.ManaGenerated.Black.Should().Be(0);
        green.ManaGenerated.Red.Should().Be(0);
    }

    [Fact]
    public void WastewoodVerge_BlackManaAbility_ProducesOnlyBlack()
    {
        var land = WastewoodVergeFactory.Create(_alice);
        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        black.ManaGenerated.Generic.Should().Be(0);
        black.ManaGenerated.White.Should().Be(0);
        black.ManaGenerated.Blue.Should().Be(0);
        black.ManaGenerated.Green.Should().Be(0);
        black.ManaGenerated.Red.Should().Be(0);
    }

    [Fact]
    public void WastewoodVerge_HasNoTriggeredAbilities()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Wastewood Verge has no triggered abilities");
    }

    [Fact]
    public void WastewoodVerge_HasNoActivatedAbilities()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Wastewood Verge has no non-mana activated abilities in v1");
    }
}
