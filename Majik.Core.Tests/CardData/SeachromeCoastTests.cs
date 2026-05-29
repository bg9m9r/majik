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
/// Unit tests for <see cref="SeachromeCoastFactory"/> — Scars of Mirrodin W/U
/// fastland.
///
/// Covers card identity, the two mana abilities ({W} + {U}), and that no
/// triggered or non-mana activated abilities ship in v1 (the conditional
/// ETB-tapped is a replacement effect handled by the binder layer in
/// production). Mirrors <see cref="SpirebluffCanalTests"/>.
/// </summary>
public class SeachromeCoastTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SeachromeCoast_IsLand()
    {
        var land = SeachromeCoastFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void SeachromeCoast_NameIsCorrect()
    {
        var land = SeachromeCoastFactory.Create(_alice);

        land.Name.Should().Be("Seachrome Coast");
    }

    [Fact]
    public void SeachromeCoast_OwnerAndControllerAreSet()
    {
        var land = SeachromeCoastFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SeachromeCoast_IsNotLegendary()
    {
        var land = SeachromeCoastFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void SeachromeCoast_HasTwoManaAbilities()
    {
        var land = SeachromeCoastFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void SeachromeCoast_HasWhiteManaAbility()
    {
        var land = SeachromeCoastFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void SeachromeCoast_HasBlueManaAbility()
    {
        var land = SeachromeCoastFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void SeachromeCoast_HasNoTriggeredAbilities()
    {
        var land = SeachromeCoastFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void SeachromeCoast_HasNoActivatedAbilities()
    {
        var land = SeachromeCoastFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void SeachromeCoast_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Seachrome Coast", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Seachrome Coast");
    }
}
