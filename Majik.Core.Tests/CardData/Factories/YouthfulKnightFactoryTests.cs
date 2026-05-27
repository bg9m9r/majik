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
/// Unit tests for <see cref="YouthfulKnightFactory"/>.
///
/// Card: Youthful Knight — {1}{W} Creature — Human Knight 2/1.
///   "First strike"
/// </summary>
public class YouthfulKnightFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void YouthfulKnight_Identity()
    {
        var c = YouthfulKnightFactory.Create(_alice);

        c.Name.Should().Be("Youthful Knight");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void YouthfulKnight_IsWhite()
    {
        var c = YouthfulKnightFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.White,
            "Youthful Knight has a {W} pip in its mana cost");
    }

    [Fact]
    public void YouthfulKnight_ManaValueIsTwo()
    {
        var c = YouthfulKnightFactory.Create(_alice);

        // {1}{W} → generic 1 + one coloured pip = mana value 2 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(2);
    }

    [Fact]
    public void YouthfulKnight_HasFirstStrikeKeywordMarker()
    {
        var c = YouthfulKnightFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "First strike").Should().BeTrue(
                "Youthful Knight ships with First strike as a KeywordAbility marker (CR 702.7)");
    }

    [Fact]
    public void YouthfulKnight_NoOtherAbilities()
    {
        var c = YouthfulKnightFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "First strike is the only printed keyword");
    }

    [Fact]
    public void YouthfulKnight_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Youthful Knight", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Youthful Knight");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
    }
}
