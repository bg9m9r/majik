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
/// Unit tests for <see cref="GarruksCompanionFactory"/>.
///
/// Card: Garruk's Companion — Creature — Beast {G}{G} 3/2 with Trample.
/// Oracle text: "Trample"
/// </summary>
[Trait("Color", "G")]
public class GarruksCompanionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GarruksCompanion_Identity()
    {
        var c = GarruksCompanionFactory.Create(_alice);

        c.Name.Should().Be("Garruk's Companion");
        c.ManaCost.Should().Be("{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GarruksCompanion_ManaValue_IsTwo()
    {
        var c = GarruksCompanionFactory.Create(_alice);

        c.ManaCostValue.TotalValue.Should().Be(2,
            "mana value 2: two Green pips, no generic mana");
    }

    [Fact]
    public void GarruksCompanion_Colors_ContainsGreenOnly()
    {
        var c = GarruksCompanionFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green, "Garruk's Companion costs {G}{G}");
        colors.Should().HaveCount(1, "Garruk's Companion is exactly Green");
    }

    [Fact]
    public void GarruksCompanion_HasTrampleKeyword()
    {
        var c = GarruksCompanionFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Trample",
                "Garruk's Companion has printed Trample (CR 702.19)");
    }

    [Fact]
    public void GarruksCompanion_NoOtherAbilities()
    {
        var c = GarruksCompanionFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Garruk's Companion has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Garruk's Companion has no activated abilities");
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Only Trample — no other keyword abilities");
    }
}
