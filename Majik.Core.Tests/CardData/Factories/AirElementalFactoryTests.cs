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
/// Unit tests for <see cref="AirElementalFactory"/>.
///
/// Card: Air Elemental — {3}{U}{U} Creature — Elemental 4/4.
///   "Flying"
/// </summary>
public class AirElementalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AirElemental_Identity()
    {
        var c = AirElementalFactory.Create(_alice);

        c.Name.Should().Be("Air Elemental");
        c.ManaCost.Should().Be("{3}{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AirElemental_IsBlue()
    {
        var c = AirElementalFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Blue,
            "Air Elemental has two {U} pips in its mana cost");
    }

    [Fact]
    public void AirElemental_ManaValueIsFive()
    {
        var c = AirElementalFactory.Create(_alice);

        // {3}{U}{U} → generic 3 + two coloured pips = mana value 5 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(5);
    }

    [Fact]
    public void AirElemental_HasFlyingKeywordMarker()
    {
        var c = AirElementalFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Air Elemental ships with Flying as a KeywordAbility marker (CR 702.9)");
    }

    [Fact]
    public void AirElemental_NoOtherAbilities()
    {
        var c = AirElementalFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Flying is the only printed keyword");
    }

    [Fact]
    public void AirElemental_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Air Elemental", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Air Elemental");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
    }
}
