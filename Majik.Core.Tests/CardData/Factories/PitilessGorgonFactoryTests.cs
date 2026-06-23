using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PitilessGorgonFactory"/>.
///
/// Card: Pitiless Gorgon — {1}{B/G}{B/G} Creature — Gorgon 2/2.
///   "Deathtouch"
/// Multicolour (black + green) via two {B/G} hybrid pips.
/// </summary>
[Trait("Color", "M")]
public class PitilessGorgonFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void PitilessGorgon_Identity()
    {
        var c = PitilessGorgonFactory.Create(_alice);

        c.Name.Should().Be("Pitiless Gorgon");
        c.ManaCost.Should().Be("{1}{B/G}{B/G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Gorgon).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PitilessGorgon_IsBlackAndGreen()
    {
        var c = PitilessGorgonFactory.Create(_alice);

        // Two {B/G} hybrid pips → the card is both black and green
        // (CR 105.1 / CR 202.2c). CardColors derives both from the hybrid pips.
        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Black);
        colors.Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void PitilessGorgon_ManaValueIsThree()
    {
        var c = PitilessGorgonFactory.Create(_alice);

        // {1}{B/G}{B/G} → generic 1 + two hybrid pips (each contributes 1) =
        // mana value 3 (CR 202.3 / CR 107.4e).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(3);
    }

    [Fact]
    public void PitilessGorgon_HasDeathtouchKeywordMarker()
    {
        var c = PitilessGorgonFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Deathtouch").Should().BeTrue(
                "Pitiless Gorgon has Deathtouch (CR 702.2)");
    }

    [Fact]
    public void PitilessGorgon_HasExactlyOneKeyword()
    {
        var c = PitilessGorgonFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Deathtouch is the only printed keyword");
    }

    [Fact]
    public void PitilessGorgon_NoTriggeredOrActivatedAbilities()
    {
        var c = PitilessGorgonFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
