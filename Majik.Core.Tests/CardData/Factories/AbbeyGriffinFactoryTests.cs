using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AbbeyGriffinFactory"/>.
///
/// Card: Abbey Griffin — {3}{W} Creature — Griffin 2/2.
///   "Flying, vigilance"
/// </summary>
[Trait("Color", "W")]
public class AbbeyGriffinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AbbeyGriffin_Identity()
    {
        var c = AbbeyGriffinFactory.Create(_alice);

        c.Name.Should().Be("Abbey Griffin");
        c.ManaCost.Should().Be("{3}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Griffin).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AbbeyGriffin_HasFlyingKeywordMarker()
    {
        var c = AbbeyGriffinFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Abbey Griffin ships with Flying as a KeywordAbility marker (CR 702.9)");
    }

    [Fact]
    public void AbbeyGriffin_HasVigilanceKeywordMarker()
    {
        var c = AbbeyGriffinFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Vigilance").Should().BeTrue(
                "Abbey Griffin ships with Vigilance as a KeywordAbility marker (CR 702.20)");
    }

    [Fact]
    public void AbbeyGriffin_HasExactlyFlyingAndVigilance()
    {
        var c = AbbeyGriffinFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "Flying and Vigilance are the only printed keywords");
    }
}
