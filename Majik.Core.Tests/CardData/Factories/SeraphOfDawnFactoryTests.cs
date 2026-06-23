using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SeraphOfDawnFactory"/>.
///
/// Card: Seraph of Dawn — {2}{W}{W} Creature — Angel 2/4.
///   "Flying, lifelink"
/// </summary>
[Trait("Color", "W")]
public class SeraphOfDawnFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SeraphOfDawn_Identity()
    {
        var c = SeraphOfDawnFactory.Create(_alice);

        c.Name.Should().Be("Seraph of Dawn");
        c.ManaCost.Should().Be("{2}{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SeraphOfDawn_HasFlyingKeywordMarker()
    {
        var c = SeraphOfDawnFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Seraph of Dawn has Flying (CR 702.9)");
    }

    [Fact]
    public void SeraphOfDawn_HasLifelinkKeywordMarker()
    {
        var c = SeraphOfDawnFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Lifelink").Should().BeTrue(
                "Seraph of Dawn has Lifelink (CR 702.15)");
    }

    [Fact]
    public void SeraphOfDawn_HasExactlyTwoKeywords()
    {
        var c = SeraphOfDawnFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "Flying and Lifelink are the only printed keywords");
    }

    [Fact]
    public void SeraphOfDawn_NoTriggeredOrActivatedAbilities()
    {
        var c = SeraphOfDawnFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
