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
/// Unit tests for <see cref="WindDrakeFactory"/>.
///
/// Card: Wind Drake — {2}{U} Creature — Drake 2/2.
///   "Flying"
/// </summary>
public class WindDrakeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void WindDrake_Identity()
    {
        var c = WindDrakeFactory.Create(_alice);

        c.Name.Should().Be("Wind Drake");
        c.ManaCost.Should().Be("{2}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Drake).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WindDrake_IsBlue()
    {
        var c = WindDrakeFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Blue,
            "Wind Drake has a {U} pip in its mana cost");
    }

    [Fact]
    public void WindDrake_ManaValueIsThree()
    {
        var c = WindDrakeFactory.Create(_alice);

        // {2}{U} → generic 2 + one coloured pip = mana value 3 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(3);
    }

    [Fact]
    public void WindDrake_HasFlyingKeywordMarker()
    {
        var c = WindDrakeFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Wind Drake ships with Flying as a KeywordAbility marker (CR 702.9)");
    }

    [Fact]
    public void WindDrake_NoOtherAbilities()
    {
        var c = WindDrakeFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Flying is the only printed keyword");
    }

    [Fact]
    public void WindDrake_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Wind Drake", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Wind Drake");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Drake).Should().BeTrue();
    }
}
