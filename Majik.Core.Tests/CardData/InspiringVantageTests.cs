using Majik.Core.CardData;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="InspiringVantageFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, non-legendary)
/// - Two mana abilities ({R} + {W})
/// - No triggered or non-mana activated abilities in v1
///   (ETB-tapped conditional handled by the binder layer in production)
/// </summary>
public class InspiringVantageTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void InspiringVantage_IsLand()
    {
        var land = (Land)NamedCardFactory.Create("Inspiring Vantage", _alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void InspiringVantage_NameIsCorrect()
    {
        var land = (Land)NamedCardFactory.Create("Inspiring Vantage", _alice);

        land.Name.Should().Be("Inspiring Vantage");
    }

    [Fact]
    public void InspiringVantage_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Inspiring Vantage", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void InspiringVantage_IsNotLegendary()
    {
        var land = (Land)NamedCardFactory.Create("Inspiring Vantage", _alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void InspiringVantage_HasTwoManaAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Inspiring Vantage", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void InspiringVantage_HasRedManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Inspiring Vantage", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void InspiringVantage_HasWhiteManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Inspiring Vantage", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void InspiringVantage_HasNoTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Inspiring Vantage", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void InspiringVantage_HasNoActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Inspiring Vantage", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
