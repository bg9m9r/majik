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
/// Unit tests for <see cref="SeveredLegionFactory"/>.
///
/// Card: Severed Legion — {1}{B}{B} Creature — Zombie 2/2.
///   "Fear (This creature can't be blocked except by artifact creatures
///    and/or black creatures.)"
/// </summary>
[Trait("Color", "B")]
public class SeveredLegionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SeveredLegion_Identity()
    {
        var c = SeveredLegionFactory.Create(_alice);

        c.Name.Should().Be("Severed Legion");
        c.ManaCost.Should().Be("{1}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SeveredLegion_IsBlack()
    {
        var c = SeveredLegionFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Black,
            "Severed Legion has {B}{B} pips in its mana cost");
    }

    [Fact]
    public void SeveredLegion_ManaValueIsThree()
    {
        var c = SeveredLegionFactory.Create(_alice);

        // {1}{B}{B} → 1 generic + 2 coloured = mana value 3 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(3);
    }

    [Fact]
    public void SeveredLegion_HasFearKeywordMarker()
    {
        var c = SeveredLegionFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Fear").Should().BeTrue(
                "Severed Legion has Fear (CR 702.36)");
    }

    [Fact]
    public void SeveredLegion_HasExactlyOneKeyword()
    {
        var c = SeveredLegionFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Fear is the only printed keyword");
    }

    [Fact]
    public void SeveredLegion_NoTriggeredOrActivatedAbilities()
    {
        var c = SeveredLegionFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
