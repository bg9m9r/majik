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
/// Unit tests for <see cref="GiantSpiderFactory"/>.
///
/// Card: Giant Spider — {3}{G} Creature — Spider 2/4.
///   "Reach" (CR 702.17)
/// </summary>
public class GiantSpiderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GiantSpider_Identity()
    {
        var c = GiantSpiderFactory.Create(_alice);

        c.Name.Should().Be("Giant Spider");
        c.ManaCost.Should().Be("{3}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spider).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GiantSpider_IsGreen()
    {
        var c = GiantSpiderFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Green,
            "Giant Spider has a {G} pip in its mana cost");
    }

    [Fact]
    public void GiantSpider_ManaValueIsFour()
    {
        var c = GiantSpiderFactory.Create(_alice);

        // {3}{G} → generic 3 + one coloured pip = mana value 4 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(4);
    }

    [Fact]
    public void GiantSpider_HasReachKeywordMarker()
    {
        var c = GiantSpiderFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Reach").Should().BeTrue(
                "Giant Spider has Reach as a KeywordAbility marker (CR 702.17)");
    }

    [Fact]
    public void GiantSpider_NoOtherAbilities()
    {
        var c = GiantSpiderFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Reach is the only printed keyword");
    }

    [Fact]
    public void GiantSpider_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Giant Spider", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Giant Spider");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spider).Should().BeTrue();
    }
}
