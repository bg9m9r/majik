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
/// Unit tests for <see cref="AlpineWatchdogFactory"/>.
///
/// Card: Alpine Watchdog — {1}{W} Creature — Dog 2/2.
///   "Vigilance"
/// </summary>
[Trait("Color", "W")]
public class AlpineWatchdogFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AlpineWatchdog_Identity()
    {
        var c = AlpineWatchdogFactory.Create(_alice);

        c.Name.Should().Be("Alpine Watchdog");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dog).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AlpineWatchdog_IsWhite()
    {
        var c = AlpineWatchdogFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.White,
            "Alpine Watchdog has {W} in its mana cost (CR 105.1)");
    }

    [Fact]
    public void AlpineWatchdog_ManaValueIsTwo()
    {
        var c = AlpineWatchdogFactory.Create(_alice);

        // {1}{W} → generic 1 + one white pip = mana value 2 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(2);
    }

    [Fact]
    public void AlpineWatchdog_HasVigilanceKeywordMarker()
    {
        var c = AlpineWatchdogFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Vigilance").Should().BeTrue(
                "Alpine Watchdog has Vigilance as a KeywordAbility marker (CR 702.20)");
    }

    [Fact]
    public void AlpineWatchdog_NoOtherAbilities()
    {
        var c = AlpineWatchdogFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Vigilance is the only printed keyword");
    }
}
