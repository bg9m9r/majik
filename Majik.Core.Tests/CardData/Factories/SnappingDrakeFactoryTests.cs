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
/// Unit tests for <see cref="SnappingDrakeFactory"/>.
///
/// Card: Snapping Drake — {3}{U} Creature — Drake 3/2.
///   "Flying"
/// </summary>
public class SnappingDrakeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SnappingDrake_Identity()
    {
        var c = SnappingDrakeFactory.Create(_alice);

        c.Name.Should().Be("Snapping Drake");
        c.ManaCost.Should().Be("{3}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Drake).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SnappingDrake_IsBlue()
    {
        var c = SnappingDrakeFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Blue,
            "Snapping Drake has a {U} pip in its mana cost");
    }

    [Fact]
    public void SnappingDrake_ManaValueIsFour()
    {
        var c = SnappingDrakeFactory.Create(_alice);

        // {3}{U} → generic 3 + one coloured pip = mana value 4 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(4);
    }

    [Fact]
    public void SnappingDrake_HasFlyingKeywordMarker()
    {
        var c = SnappingDrakeFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Snapping Drake ships with Flying as a KeywordAbility marker (CR 702.9)");
    }

    [Fact]
    public void SnappingDrake_NoOtherAbilities()
    {
        var c = SnappingDrakeFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Flying is the only printed keyword");
    }

    [Fact]
    public void SnappingDrake_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Snapping Drake", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Snapping Drake");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Drake).Should().BeTrue();
    }
}
