using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SundownPassFactory"/> — Streets of New Capenna
/// R/W slowland.
///
/// Covers card identity, the two mana abilities ({R} + {W}), and that no
/// triggered or non-mana activated abilities ship in v1 (the conditional
/// ETB-tapped — "two or more other lands", CR 614.1c — is a replacement
/// effect handled by the binder layer in production).
/// </summary>
public class SundownPassTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SundownPass_IsLand()
    {
        var land = SundownPassFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void SundownPass_NameIsCorrect()
    {
        var land = SundownPassFactory.Create(_alice);

        land.Name.Should().Be("Sundown Pass");
    }

    [Fact]
    public void SundownPass_OwnerAndControllerAreSet()
    {
        var land = SundownPassFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SundownPass_IsNotLegendary()
    {
        var land = SundownPassFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void SundownPass_HasTwoManaAbilities()
    {
        var land = SundownPassFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void SundownPass_HasRedManaAbility()
    {
        var land = SundownPassFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void SundownPass_HasWhiteManaAbility()
    {
        var land = SundownPassFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void SundownPass_HasNoTriggeredAbilities()
    {
        var land = SundownPassFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void SundownPass_HasNoActivatedAbilities()
    {
        var land = SundownPassFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void SundownPass_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Sundown Pass", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Sundown Pass");
    }
}
