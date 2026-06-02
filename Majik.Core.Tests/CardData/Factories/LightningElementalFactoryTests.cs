using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LightningElementalFactory"/>.
///
/// Card: Lightning Elemental — {3}{R} Creature — Elemental 4/1.
///   "Haste"
/// </summary>
[Trait("Color", "R")]
public class LightningElementalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void LightningElemental_Identity()
    {
        var c = LightningElementalFactory.Create(_alice);

        c.Name.Should().Be("Lightning Elemental");
        c.ManaCost.Should().Be("{3}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LightningElemental_IsRed()
    {
        var c = LightningElementalFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Red,
            "Lightning Elemental has one {R} pip in its mana cost");
    }

    [Fact]
    public void LightningElemental_ManaValueIsFour()
    {
        var c = LightningElementalFactory.Create(_alice);

        // {3}{R} → generic 3 + one coloured pip = mana value 4 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(4,
            "{3}{R} has a total mana value of 4 (CR 202.3)");
    }

    [Fact]
    public void LightningElemental_HasHasteKeywordMarker()
    {
        var c = LightningElementalFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(a => a.Keyword == "Haste",
                "Lightning Elemental has Haste (CR 702.10)");
    }

    [Fact]
    public void LightningElemental_NoOtherAbilities()
    {
        var c = LightningElementalFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Haste is the only printed keyword");
    }
}
