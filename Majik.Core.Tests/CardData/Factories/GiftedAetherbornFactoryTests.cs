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
/// Unit tests for <see cref="GiftedAetherbornFactory"/>.
///
/// Card: Gifted Aetherborn — {B}{B} Creature — Aetherborn Vampire 2/3.
///   "Deathtouch, lifelink"
/// </summary>
[Trait("Color", "B")]
public class GiftedAetherbornFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GiftedAetherborn_Identity()
    {
        var c = GiftedAetherbornFactory.Create(_alice);

        c.Name.Should().Be("Gifted Aetherborn");
        c.ManaCost.Should().Be("{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aetherborn).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GiftedAetherborn_IsBlack()
    {
        var c = GiftedAetherbornFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Black,
            "Gifted Aetherborn has {B}{B} pips in its mana cost");
    }

    [Fact]
    public void GiftedAetherborn_ManaValueIsTwo()
    {
        var c = GiftedAetherbornFactory.Create(_alice);

        // {B}{B} → two coloured pips = mana value 2 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(2);
    }

    [Fact]
    public void GiftedAetherborn_HasDeathtouchKeywordMarker()
    {
        var c = GiftedAetherbornFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Deathtouch").Should().BeTrue(
                "Gifted Aetherborn has Deathtouch (CR 702.2)");
    }

    [Fact]
    public void GiftedAetherborn_HasLifelinkKeywordMarker()
    {
        var c = GiftedAetherbornFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Lifelink").Should().BeTrue(
                "Gifted Aetherborn has Lifelink (CR 702.15)");
    }

    [Fact]
    public void GiftedAetherborn_HasExactlyTwoKeywords()
    {
        var c = GiftedAetherbornFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "Deathtouch and Lifelink are the only printed keywords (no Flying)");
    }

    [Fact]
    public void GiftedAetherborn_HasNoFlying()
    {
        var c = GiftedAetherbornFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeFalse(
                "Gifted Aetherborn does not have Flying (unlike Vampire Nighthawk)");
    }

    [Fact]
    public void GiftedAetherborn_NoTriggeredOrActivatedAbilities()
    {
        var c = GiftedAetherbornFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
