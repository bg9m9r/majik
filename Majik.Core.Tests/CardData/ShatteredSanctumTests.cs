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
/// Unit tests for <see cref="ShatteredSanctumFactory"/> — Innistrad: Crimson
/// Vow W/B slowland.
///
/// Covers card identity, the two mana abilities ({W} + {B}), and that no
/// triggered or non-mana activated abilities ship in v1 (the conditional
/// ETB-tapped "two or more other lands" is a replacement effect handled by
/// the binder layer in production — CR 614.1c). Mirrors
/// <see cref="DesertedBeachTests"/> (same slowland cycle).
/// </summary>
public class ShatteredSanctumTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ShatteredSanctum_IsLand()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void ShatteredSanctum_NameIsCorrect()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.Name.Should().Be("Shattered Sanctum");
    }

    [Fact]
    public void ShatteredSanctum_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ShatteredSanctum_IsNotLegendary()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void ShatteredSanctum_HasTwoManaAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void ShatteredSanctum_HasWhiteManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void ShatteredSanctum_HasBlackManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void ShatteredSanctum_HasNoTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void ShatteredSanctum_HasNoActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void ShatteredSanctum_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Shattered Sanctum", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Shattered Sanctum");
    }
}
