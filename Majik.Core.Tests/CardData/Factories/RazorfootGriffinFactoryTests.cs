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
/// Unit tests for <see cref="RazorfootGriffinFactory"/>.
///
/// Card: Razorfoot Griffin — {3}{W} Creature — Griffin 2/2.
///   "Flying"
///   "First strike"
/// </summary>
[Trait("Color", "W")]
public class RazorfootGriffinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RazorfootGriffin_Identity()
    {
        var c = RazorfootGriffinFactory.Create(_alice);

        c.Name.Should().Be("Razorfoot Griffin");
        c.ManaCost.Should().Be("{3}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Griffin).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RazorfootGriffin_IsWhite()
    {
        var c = RazorfootGriffinFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.White,
            "Razorfoot Griffin has a {W} pip in its mana cost");
    }

    [Fact]
    public void RazorfootGriffin_ManaValueIsFour()
    {
        var c = RazorfootGriffinFactory.Create(_alice);

        // {3}{W} → generic 3 + one coloured pip = mana value 4 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(4);
    }

    [Fact]
    public void RazorfootGriffin_HasFlyingKeywordMarker()
    {
        var c = RazorfootGriffinFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Razorfoot Griffin has Flying (CR 702.9)");
    }

    [Fact]
    public void RazorfootGriffin_HasFirstStrikeKeywordMarker()
    {
        var c = RazorfootGriffinFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "First Strike").Should().BeTrue(
                "Razorfoot Griffin has First Strike (CR 702.7)");
    }

    [Fact]
    public void RazorfootGriffin_HasExactlyTwoKeywordAbilities()
    {
        var c = RazorfootGriffinFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();

        keywords.Should().BeEquivalentTo(
            new[] { "Flying", "First Strike" },
            "Flying and First Strike are the only printed keywords");
    }

    [Fact]
    public void RazorfootGriffin_NoTriggeredOrActivatedAbilities()
    {
        var c = RazorfootGriffinFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
