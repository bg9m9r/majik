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
/// Unit tests for <see cref="DesertedBeachFactory"/> — Innistrad: Midnight
/// Hunt W/U slowland.
///
/// Covers card identity, the two mana abilities ({W} + {U}), and that no
/// triggered or non-mana activated abilities ship in v1 (the conditional
/// ETB-tapped "two or more other lands" is a replacement effect handled by
/// the binder layer in production — CR 614.1c). Mirrors
/// <see cref="StormcarvedCoastTests"/> (same slowland cycle).
/// </summary>
public class DesertedBeachTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DesertedBeach_IsLand()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void DesertedBeach_NameIsCorrect()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.Name.Should().Be("Deserted Beach");
    }

    [Fact]
    public void DesertedBeach_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DesertedBeach_IsNotLegendary()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void DesertedBeach_HasTwoManaAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void DesertedBeach_HasWhiteManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void DesertedBeach_HasBlueManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void DesertedBeach_HasNoTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void DesertedBeach_HasNoActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void DesertedBeach_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Deserted Beach", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Deserted Beach");
    }
}
