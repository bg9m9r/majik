using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SkyknightLegionnaireFactory"/>
/// (Ravnica: City of Guilds, {1}{R}{W}).
///
/// Covers:
/// - Card identity: name, Creature type, Human + Knight subtypes, 2/2 P/T,
///   mana cost {1}{R}{W}, mana value 3, owner / controller wiring.
/// - Colours derived from cost: Red AND White (CR 202.2).
/// - Flying keyword marker (CR 702.9).
/// - Haste keyword marker (CR 702.10).
/// - <see cref="CombatAbilities"/> lookups: HasFlying + HasHaste are true.
/// - <see cref="NamedCardFactory"/> dispatch routes the card name correctly.
/// </summary>
public class SkyknightLegionnaireFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SkyknightLegionnaire_Name_IsCorrect()
    {
        var c = SkyknightLegionnaireFactory.Create(_alice);
        c.Name.Should().Be("Skyknight Legionnaire");
    }

    [Fact]
    public void SkyknightLegionnaire_IsCreature()
    {
        var c = SkyknightLegionnaireFactory.Create(_alice);
        c.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void SkyknightLegionnaire_HasHumanAndKnightSubtypes()
    {
        var c = SkyknightLegionnaireFactory.Create(_alice);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
    }

    [Fact]
    public void SkyknightLegionnaire_PowerAndToughness_Are_2_2()
    {
        var c = SkyknightLegionnaireFactory.Create(_alice);
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
    }

    [Fact]
    public void SkyknightLegionnaire_ManaCost_Is_1RW()
    {
        var c = SkyknightLegionnaireFactory.Create(_alice);
        c.ManaCost.Should().Be("{1}{R}{W}");
    }

    [Fact]
    public void SkyknightLegionnaire_ManaValue_IsThree()
    {
        var c = SkyknightLegionnaireFactory.Create(_alice);
        c.ManaCostValue.TotalValue.Should().Be(3);
    }

    [Fact]
    public void SkyknightLegionnaire_OwnerAndControllerAreSet()
    {
        var c = SkyknightLegionnaireFactory.Create(_alice);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SkyknightLegionnaire_ColorsContainRedAndWhite()
    {
        var c = SkyknightLegionnaireFactory.Create(_alice);
        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Red,
            "mana cost {1}{R}{W} includes a red pip (CR 202.2)");
        colors.Should().Contain(ManaColor.White,
            "mana cost {1}{R}{W} includes a white pip (CR 202.2)");
    }

    [Fact]
    public void SkyknightLegionnaire_HasFlyingKeyword()
    {
        var c = SkyknightLegionnaireFactory.Create(_alice);
        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "Skyknight Legionnaire has the printed Flying ability (CR 702.9)");
    }

    [Fact]
    public void SkyknightLegionnaire_HasHasteKeyword()
    {
        var c = SkyknightLegionnaireFactory.Create(_alice);
        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Haste",
                "Skyknight Legionnaire has the printed Haste ability (CR 702.10)");
    }

    [Fact]
    public void SkyknightLegionnaire_CombatAbilities_FlyingAndHasteAreTrue()
    {
        var c = SkyknightLegionnaireFactory.Create(_alice);
        CombatAbilities.HasFlying(c).Should().BeTrue();
        CombatAbilities.HasHaste(c).Should().BeTrue();
    }

    [Fact]
    public void SkyknightLegionnaire_HasExactlyTwoKeywordAbilities()
    {
        var c = SkyknightLegionnaireFactory.Create(_alice);
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "exactly Flying and Haste — no other keyword markers on Skyknight Legionnaire");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SkyknightLegionnaire()
    {
        var card = NamedCardFactory.Create("Skyknight Legionnaire", _alice);

        card.Should().NotBeNull();
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Skyknight Legionnaire");
        ((Creature)card).Power.Should().Be(2);
        ((Creature)card).Toughness.Should().Be(2);
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Knight).Should().BeTrue();
    }
}
