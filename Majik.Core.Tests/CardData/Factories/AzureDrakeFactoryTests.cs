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
/// Unit tests for <see cref="AzureDrakeFactory"/>.
///
/// Card: Azure Drake — {3}{U} Creature — Drake 2/4.
///   "Flying"
/// </summary>
public class AzureDrakeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AzureDrake_Identity()
    {
        var c = AzureDrakeFactory.Create(_alice);

        c.Name.Should().Be("Azure Drake");
        c.ManaCost.Should().Be("{3}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Drake).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AzureDrake_IsBlue()
    {
        var c = AzureDrakeFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Blue,
            "Azure Drake has a {U} pip in its mana cost");
    }

    [Fact]
    public void AzureDrake_ManaValueIsFour()
    {
        var c = AzureDrakeFactory.Create(_alice);

        // {3}{U} → generic 3 + one coloured pip = mana value 4 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(4);
    }

    [Fact]
    public void AzureDrake_HasFlyingKeywordMarker()
    {
        var c = AzureDrakeFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Azure Drake ships with Flying as a KeywordAbility marker (CR 702.9)");
    }

    [Fact]
    public void AzureDrake_NoOtherAbilities()
    {
        var c = AzureDrakeFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Flying is the only printed keyword");
    }

    [Fact]
    public void AzureDrake_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Azure Drake", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Azure Drake");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Drake).Should().BeTrue();
    }
}
