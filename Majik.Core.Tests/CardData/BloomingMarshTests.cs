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
/// Unit tests for <see cref="BloomingMarshFactory"/> — Kaladesh B/G fastland.
///
/// Covers card identity, the two mana abilities ({B} + {G}), and that no
/// triggered or non-mana activated abilities ship in v1 (the conditional
/// ETB-tapped is a replacement effect handled by the binder layer in
/// production).
/// </summary>
public class BloomingMarshTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BloomingMarsh_IsLand()
    {
        var land = (Land)NamedCardFactory.Create("Blooming Marsh", _alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void BloomingMarsh_NameIsCorrect()
    {
        var land = (Land)NamedCardFactory.Create("Blooming Marsh", _alice);

        land.Name.Should().Be("Blooming Marsh");
    }

    [Fact]
    public void BloomingMarsh_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Blooming Marsh", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BloomingMarsh_IsNotLegendary()
    {
        var land = (Land)NamedCardFactory.Create("Blooming Marsh", _alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void BloomingMarsh_HasTwoManaAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Blooming Marsh", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void BloomingMarsh_HasBlackManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Blooming Marsh", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void BloomingMarsh_HasGreenManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Blooming Marsh", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void BloomingMarsh_HasNoTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Blooming Marsh", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void BloomingMarsh_HasNoActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Blooming Marsh", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void BloomingMarsh_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Blooming Marsh", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Blooming Marsh");
    }
}
