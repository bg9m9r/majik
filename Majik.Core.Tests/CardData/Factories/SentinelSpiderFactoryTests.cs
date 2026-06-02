using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SentinelSpiderFactory"/>.
///
/// Card: Sentinel Spider — {3}{G}{G} Creature — Spider 4/4.
///   "Vigilance, reach"
/// </summary>
[Trait("Color", "G")]
public class SentinelSpiderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SentinelSpider_Identity()
    {
        var c = SentinelSpiderFactory.Create(_alice);

        c.Name.Should().Be("Sentinel Spider");
        c.ManaCost.Should().Be("{3}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spider).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SentinelSpider_IsGreen()
    {
        var c = SentinelSpiderFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Green,
            "Sentinel Spider has {G}{G} pips in its mana cost");
    }

    [Fact]
    public void SentinelSpider_ManaValueIsFive()
    {
        var c = SentinelSpiderFactory.Create(_alice);

        // {3}{G}{G} → generic 3 + two green pips = mana value 5 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(5);
    }

    [Fact]
    public void SentinelSpider_HasVigilanceKeywordMarker()
    {
        var c = SentinelSpiderFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Vigilance").Should().BeTrue(
                "Sentinel Spider has Vigilance as a KeywordAbility marker (CR 702.20)");
    }

    [Fact]
    public void SentinelSpider_HasReachKeywordMarker()
    {
        var c = SentinelSpiderFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Reach").Should().BeTrue(
                "Sentinel Spider has Reach as a KeywordAbility marker (CR 702.17)");
    }

    [Fact]
    public void SentinelSpider_NoOtherAbilities()
    {
        var c = SentinelSpiderFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "Vigilance and Reach are the only printed keywords");
    }
}
