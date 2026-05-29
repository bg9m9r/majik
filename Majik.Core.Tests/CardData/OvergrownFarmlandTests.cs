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
/// Unit tests for <see cref="OvergrownFarmlandFactory"/> — Innistrad: Midnight
/// Hunt G/W slowland.
///
/// Covers card identity, the two mana abilities ({G} + {W}), and that no
/// triggered or non-mana activated abilities ship in v1 (the conditional
/// ETB-tapped "two or more other lands" is a replacement effect handled by
/// the binder layer in production — CR 614.1c). Mirrors
/// <see cref="DesertedBeachTests"/> (same slowland cycle).
/// </summary>
public class OvergrownFarmlandTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void OvergrownFarmland_IsLand()
    {
        var land = OvergrownFarmlandFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void OvergrownFarmland_NameIsCorrect()
    {
        var land = OvergrownFarmlandFactory.Create(_alice);

        land.Name.Should().Be("Overgrown Farmland");
    }

    [Fact]
    public void OvergrownFarmland_OwnerAndControllerAreSet()
    {
        var land = OvergrownFarmlandFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void OvergrownFarmland_IsNotLegendary()
    {
        var land = OvergrownFarmlandFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void OvergrownFarmland_HasTwoManaAbilities()
    {
        var land = OvergrownFarmlandFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void OvergrownFarmland_HasGreenManaAbility()
    {
        var land = OvergrownFarmlandFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void OvergrownFarmland_HasWhiteManaAbility()
    {
        var land = OvergrownFarmlandFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void OvergrownFarmland_HasNoTriggeredAbilities()
    {
        var land = OvergrownFarmlandFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void OvergrownFarmland_HasNoActivatedAbilities()
    {
        var land = OvergrownFarmlandFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void OvergrownFarmland_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Overgrown Farmland", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Overgrown Farmland");
    }
}
