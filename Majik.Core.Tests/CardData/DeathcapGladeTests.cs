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
/// Unit tests for <see cref="DeathcapGladeFactory"/> — Midnight Hunt B/G slowland.
///
/// Covers card identity, the two mana abilities ({B} + {G}), and that no
/// triggered or non-mana activated abilities ship in v1 (the conditional
/// ETB-tapped "two or more other lands" is a replacement effect handled by
/// the binder layer in production).
/// </summary>
public class DeathcapGladeTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DeathcapGlade_IsLand()
    {
        var land = DeathcapGladeFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void DeathcapGlade_NameIsCorrect()
    {
        var land = DeathcapGladeFactory.Create(_alice);

        land.Name.Should().Be("Deathcap Glade");
    }

    [Fact]
    public void DeathcapGlade_OwnerAndControllerAreSet()
    {
        var land = DeathcapGladeFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DeathcapGlade_IsNotLegendary()
    {
        var land = DeathcapGladeFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void DeathcapGlade_HasTwoManaAbilities()
    {
        var land = DeathcapGladeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void DeathcapGlade_HasBlackManaAbility()
    {
        var land = DeathcapGladeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void DeathcapGlade_HasGreenManaAbility()
    {
        var land = DeathcapGladeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void DeathcapGlade_HasNoTriggeredAbilities()
    {
        var land = DeathcapGladeFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-or-more-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void DeathcapGlade_HasNoActivatedAbilities()
    {
        var land = DeathcapGladeFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void DeathcapGlade_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Deathcap Glade", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Deathcap Glade");
    }
}
