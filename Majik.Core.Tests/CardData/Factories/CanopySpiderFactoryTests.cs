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
/// Unit tests for <see cref="CanopySpiderFactory"/>.
///
/// Card: Canopy Spider — {1}{G} Creature — Spider 1/3.
///   "Reach" (CR 702.17)
/// </summary>
public class CanopySpiderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CanopySpider_Identity()
    {
        var c = CanopySpiderFactory.Create(_alice);

        c.Name.Should().Be("Canopy Spider");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spider).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CanopySpider_IsGreen()
    {
        var c = CanopySpiderFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Green,
            "Canopy Spider has a {G} pip in its mana cost");
    }

    [Fact]
    public void CanopySpider_ManaValueIsTwo()
    {
        var c = CanopySpiderFactory.Create(_alice);

        // {1}{G} → generic 1 + one coloured pip = mana value 2 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(2);
    }

    [Fact]
    public void CanopySpider_HasReachKeywordMarker()
    {
        var c = CanopySpiderFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Reach").Should().BeTrue(
                "Canopy Spider ships with Reach as a KeywordAbility marker (CR 702.17)");
    }

    [Fact]
    public void CanopySpider_NoOtherAbilities()
    {
        var c = CanopySpiderFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Reach is the only printed keyword");
    }

    [Fact]
    public void CanopySpider_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Canopy Spider", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Canopy Spider");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spider).Should().BeTrue();
    }
}
