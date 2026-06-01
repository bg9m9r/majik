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
/// Unit tests for <see cref="ShipwreckMarshFactory"/> — Innistrad: Midnight
/// Hunt U/B slowland.
///
/// Covers card identity, the two mana abilities ({U} + {B}), and that no
/// triggered or non-mana activated abilities ship in v1 (the conditional
/// ETB-tapped "two or more other lands" is a replacement effect handled by
/// the binder layer in production — CR 614.1c).
/// </summary>
public class ShipwreckMarshTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ShipwreckMarsh_IsLand()
    {
        var land = (Land)NamedCardFactory.Create("Shipwreck Marsh", _alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void ShipwreckMarsh_NameIsCorrect()
    {
        var land = (Land)NamedCardFactory.Create("Shipwreck Marsh", _alice);

        land.Name.Should().Be("Shipwreck Marsh");
    }

    [Fact]
    public void ShipwreckMarsh_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Shipwreck Marsh", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ShipwreckMarsh_IsNotLegendary()
    {
        var land = (Land)NamedCardFactory.Create("Shipwreck Marsh", _alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void ShipwreckMarsh_HasTwoManaAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Shipwreck Marsh", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void ShipwreckMarsh_HasBlueManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Shipwreck Marsh", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void ShipwreckMarsh_HasBlackManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Shipwreck Marsh", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void ShipwreckMarsh_HasNoTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Shipwreck Marsh", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void ShipwreckMarsh_HasNoActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Shipwreck Marsh", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void ShipwreckMarsh_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Shipwreck Marsh", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Shipwreck Marsh");
    }
}
