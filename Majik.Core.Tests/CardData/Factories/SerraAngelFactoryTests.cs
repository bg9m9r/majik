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
/// Unit tests for <see cref="SerraAngelFactory"/>.
///
/// Card: Serra Angel — {3}{W}{W} Creature — Angel 4/4.
///   "Flying, vigilance"
/// </summary>
public class SerraAngelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SerraAngel_Identity()
    {
        var c = SerraAngelFactory.Create(_alice);

        c.Name.Should().Be("Serra Angel");
        c.ManaCost.Should().Be("{3}{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SerraAngel_IsWhite()
    {
        var c = SerraAngelFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.White,
            "Serra Angel has {W}{W} pips in its mana cost");
    }

    [Fact]
    public void SerraAngel_ManaValueIsFive()
    {
        var c = SerraAngelFactory.Create(_alice);

        // {3}{W}{W} → generic 3 + two white pips = mana value 5 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(5);
    }

    [Fact]
    public void SerraAngel_HasFlyingKeywordMarker()
    {
        var c = SerraAngelFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Serra Angel has Flying as a KeywordAbility marker (CR 702.9)");
    }

    [Fact]
    public void SerraAngel_HasVigilanceKeywordMarker()
    {
        var c = SerraAngelFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Vigilance").Should().BeTrue(
                "Serra Angel has Vigilance as a KeywordAbility marker (CR 702.20)");
    }

    [Fact]
    public void SerraAngel_NoOtherAbilities()
    {
        var c = SerraAngelFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "Flying and Vigilance are the only printed keywords");
    }

    [Fact]
    public void SerraAngel_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Serra Angel", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Serra Angel");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
    }
}
