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
/// Unit tests for <see cref="BotanicalSanctumFactory"/> — Kaladesh G/U fastland.
///
/// Covers card identity, the two mana abilities ({G} + {U}), and that no
/// triggered or non-mana activated abilities ship in v1 (the conditional
/// ETB-tapped is a replacement effect handled by the binder layer in
/// production).
/// </summary>
public class BotanicalSanctumTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BotanicalSanctum_IsLand()
    {
        var land = (Land)NamedCardFactory.Create("Botanical Sanctum", _alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void BotanicalSanctum_NameIsCorrect()
    {
        var land = (Land)NamedCardFactory.Create("Botanical Sanctum", _alice);

        land.Name.Should().Be("Botanical Sanctum");
    }

    [Fact]
    public void BotanicalSanctum_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Botanical Sanctum", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BotanicalSanctum_IsNotLegendary()
    {
        var land = (Land)NamedCardFactory.Create("Botanical Sanctum", _alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void BotanicalSanctum_HasTwoManaAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Botanical Sanctum", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void BotanicalSanctum_HasGreenManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Botanical Sanctum", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void BotanicalSanctum_HasBlueManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Botanical Sanctum", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void BotanicalSanctum_HasNoTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Botanical Sanctum", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void BotanicalSanctum_HasNoActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Botanical Sanctum", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void BotanicalSanctum_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Botanical Sanctum", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Botanical Sanctum");
    }
}
