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
/// Unit tests for <see cref="StormcarvedCoastFactory"/> — Innistrad: Midnight
/// Hunt U/R slowland.
///
/// Covers card identity, the two mana abilities ({U} + {R}), and that no
/// triggered or non-mana activated abilities ship in v1 (the conditional
/// ETB-tapped "two or more other lands" is a replacement effect handled by
/// the binder layer in production — CR 614.1c).
/// </summary>
public class StormcarvedCoastTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void StormcarvedCoast_IsLand()
    {
        var land = StormcarvedCoastFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void StormcarvedCoast_NameIsCorrect()
    {
        var land = StormcarvedCoastFactory.Create(_alice);

        land.Name.Should().Be("Stormcarved Coast");
    }

    [Fact]
    public void StormcarvedCoast_OwnerAndControllerAreSet()
    {
        var land = StormcarvedCoastFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void StormcarvedCoast_IsNotLegendary()
    {
        var land = StormcarvedCoastFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void StormcarvedCoast_HasTwoManaAbilities()
    {
        var land = StormcarvedCoastFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void StormcarvedCoast_HasBlueManaAbility()
    {
        var land = StormcarvedCoastFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void StormcarvedCoast_HasRedManaAbility()
    {
        var land = StormcarvedCoastFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void StormcarvedCoast_HasNoTriggeredAbilities()
    {
        var land = StormcarvedCoastFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void StormcarvedCoast_HasNoActivatedAbilities()
    {
        var land = StormcarvedCoastFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void StormcarvedCoast_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Stormcarved Coast", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Stormcarved Coast");
    }
}
