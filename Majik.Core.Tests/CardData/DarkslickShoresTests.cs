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
/// Unit tests for <see cref="DarkslickShoresFactory"/> — Scars of Mirrodin U/B fastland.
///
/// Covers card identity, the two mana abilities ({U} + {B}), and that no
/// triggered or non-mana activated abilities ship in v1 (the conditional
/// ETB-tapped is a replacement effect handled by the binder layer in
/// production).
/// </summary>
public class DarkslickShoresTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DarkslickShores_IsLand()
    {
        var land = (Land)NamedCardFactory.Create("Darkslick Shores", _alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void DarkslickShores_NameIsCorrect()
    {
        var land = (Land)NamedCardFactory.Create("Darkslick Shores", _alice);

        land.Name.Should().Be("Darkslick Shores");
    }

    [Fact]
    public void DarkslickShores_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Darkslick Shores", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DarkslickShores_IsNotLegendary()
    {
        var land = (Land)NamedCardFactory.Create("Darkslick Shores", _alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void DarkslickShores_HasTwoManaAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Darkslick Shores", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void DarkslickShores_HasBlueManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Darkslick Shores", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void DarkslickShores_HasBlackManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Darkslick Shores", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void DarkslickShores_HasNoTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Darkslick Shores", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void DarkslickShores_HasNoActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Darkslick Shores", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void DarkslickShores_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Darkslick Shores", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Darkslick Shores");
    }
}
