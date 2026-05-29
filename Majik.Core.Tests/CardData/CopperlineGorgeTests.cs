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
/// Unit tests for <see cref="CopperlineGorgeFactory"/> — Scars of Mirrodin R/G
/// fastland.
///
/// Covers card identity, the two mana abilities ({R} + {G}), and that no
/// triggered or non-mana activated abilities ship in v1 (the conditional
/// ETB-tapped is a replacement effect handled by the binder layer in
/// production — same cycle as Spirebluff Canal / Inspiring Vantage).
/// </summary>
public class CopperlineGorgeTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CopperlineGorge_IsLand()
    {
        var land = CopperlineGorgeFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void CopperlineGorge_NameIsCorrect()
    {
        var land = CopperlineGorgeFactory.Create(_alice);

        land.Name.Should().Be("Copperline Gorge");
    }

    [Fact]
    public void CopperlineGorge_OwnerAndControllerAreSet()
    {
        var land = CopperlineGorgeFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CopperlineGorge_IsNotLegendary()
    {
        var land = CopperlineGorgeFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void CopperlineGorge_HasTwoManaAbilities()
    {
        var land = CopperlineGorgeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void CopperlineGorge_HasRedManaAbility()
    {
        var land = CopperlineGorgeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void CopperlineGorge_HasGreenManaAbility()
    {
        var land = CopperlineGorgeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void CopperlineGorge_HasNoTriggeredAbilities()
    {
        var land = CopperlineGorgeFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void CopperlineGorge_HasNoActivatedAbilities()
    {
        var land = CopperlineGorgeFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void CopperlineGorge_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Copperline Gorge", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Copperline Gorge");
    }
}
