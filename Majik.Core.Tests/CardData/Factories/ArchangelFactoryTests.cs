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
/// Unit tests for <see cref="ArchangelFactory"/>.
///
/// Card: Archangel — {5}{W}{W} Creature — Angel 5/5.
///   "Flying, vigilance"
/// </summary>
[Trait("Color", "W")]
public class ArchangelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Archangel_Identity()
    {
        var c = ArchangelFactory.Create(_alice);

        c.Name.Should().Be("Archangel");
        c.ManaCost.Should().Be("{5}{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        c.Power.Should().Be(5);
        c.Toughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Archangel_IsWhite()
    {
        var c = ArchangelFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.White,
            "Archangel has {W}{W} pips in its mana cost");
    }

    [Fact]
    public void Archangel_ManaValueIsSeven()
    {
        var c = ArchangelFactory.Create(_alice);

        // {5}{W}{W} → generic 5 + two white pips = mana value 7 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(7);
    }

    [Fact]
    public void Archangel_HasFlyingKeywordMarker()
    {
        var c = ArchangelFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Archangel has Flying as a KeywordAbility marker (CR 702.9)");
    }

    [Fact]
    public void Archangel_HasVigilanceKeywordMarker()
    {
        var c = ArchangelFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Vigilance").Should().BeTrue(
                "Archangel has Vigilance as a KeywordAbility marker (CR 702.20)");
    }

    [Fact]
    public void Archangel_NoOtherAbilities()
    {
        var c = ArchangelFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "Flying and Vigilance are the only printed keywords");
    }
}
