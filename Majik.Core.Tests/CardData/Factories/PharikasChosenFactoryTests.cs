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
/// Unit tests for <see cref="PharikasChosenFactory"/>.
///
/// Card: Pharika's Chosen — {B} Creature — Snake 1/1.
///   "Deathtouch"
/// </summary>
[Trait("Color", "B")]
public class PharikasChosenFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void PharikasChosen_Identity()
    {
        var c = PharikasChosenFactory.Create(_alice);

        c.Name.Should().Be("Pharika's Chosen");
        c.ManaCost.Should().Be("{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PharikasChosen_IsBlack()
    {
        var c = PharikasChosenFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Black,
            "Pharika's Chosen has {B} pip in its mana cost");
    }

    [Fact]
    public void PharikasChosen_ManaValueIsOne()
    {
        var c = PharikasChosenFactory.Create(_alice);

        // {B} → one coloured pip = mana value 1 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(1);
    }

    [Fact]
    public void PharikasChosen_HasDeathtouchKeywordMarker()
    {
        var c = PharikasChosenFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Deathtouch").Should().BeTrue(
                "Pharika's Chosen has Deathtouch (CR 702.2)");
    }

    [Fact]
    public void PharikasChosen_HasExactlyOneKeyword()
    {
        var c = PharikasChosenFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Deathtouch is the only printed keyword");
    }

    [Fact]
    public void PharikasChosen_NoTriggeredOrActivatedAbilities()
    {
        var c = PharikasChosenFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
