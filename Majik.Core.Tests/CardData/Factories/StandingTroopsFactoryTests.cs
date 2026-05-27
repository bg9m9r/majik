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
/// Unit tests for <see cref="StandingTroopsFactory"/>.
///
/// Card: Standing Troops — {2}{W} Creature — Human Soldier 1/4.
///   "Vigilance"
/// </summary>
public class StandingTroopsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void StandingTroops_Identity()
    {
        var c = StandingTroopsFactory.Create(_alice);

        c.Name.Should().Be("Standing Troops");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void StandingTroops_IsWhite()
    {
        var c = StandingTroopsFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.White,
            "Standing Troops has a {W} pip in its mana cost");
    }

    [Fact]
    public void StandingTroops_ManaValueIsThree()
    {
        var c = StandingTroopsFactory.Create(_alice);

        // {2}{W} → generic 2 + one coloured pip = mana value 3 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(3);
    }

    [Fact]
    public void StandingTroops_HasVigilanceKeywordMarker()
    {
        var c = StandingTroopsFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Vigilance").Should().BeTrue(
                "Standing Troops ships with Vigilance as a KeywordAbility marker (CR 702.20)");
    }

    [Fact]
    public void StandingTroops_NoOtherAbilities()
    {
        var c = StandingTroopsFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Vigilance is the only printed keyword");
    }

    [Fact]
    public void StandingTroops_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Standing Troops", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Standing Troops");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
    }
}
