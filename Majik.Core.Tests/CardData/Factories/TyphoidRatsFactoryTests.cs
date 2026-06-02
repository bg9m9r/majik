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
/// Unit tests for <see cref="TyphoidRatsFactory"/>.
///
/// Card: Typhoid Rats — {B} Creature — Rat 1/1.
///   "Deathtouch"
/// </summary>
[Trait("Color", "B")]
public class TyphoidRatsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TyphoidRats_Identity()
    {
        var c = TyphoidRatsFactory.Create(_alice);

        c.Name.Should().Be("Typhoid Rats");
        c.ManaCost.Should().Be("{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rat).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TyphoidRats_IsBlack()
    {
        var c = TyphoidRatsFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Black,
            "Typhoid Rats has {B} pip in its mana cost");
    }

    [Fact]
    public void TyphoidRats_ManaValueIsOne()
    {
        var c = TyphoidRatsFactory.Create(_alice);

        // {B} → one coloured pip = mana value 1 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(1);
    }

    [Fact]
    public void TyphoidRats_HasDeathtouchKeywordMarker()
    {
        var c = TyphoidRatsFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Deathtouch").Should().BeTrue(
                "Typhoid Rats has Deathtouch (CR 702.2)");
    }

    [Fact]
    public void TyphoidRats_HasExactlyOneKeyword()
    {
        var c = TyphoidRatsFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Deathtouch is the only printed keyword");
    }

    [Fact]
    public void TyphoidRats_NoTriggeredOrActivatedAbilities()
    {
        var c = TyphoidRatsFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
