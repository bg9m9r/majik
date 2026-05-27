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
/// Unit tests for <see cref="OnakkeOgreFactory"/>.
///
/// Card: Onakke Ogre — Creature — Ogre Warrior {2}{R} 4/2 (Magic 2013 /
/// Modern reprints). Vanilla — no printed keywords, triggers, statics, or
/// activated abilities.
/// </summary>
public class OnakkeOgreFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void OnakkeOgre_Identity()
    {
        var c = OnakkeOgreFactory.Create(_alice);

        c.Name.Should().Be("Onakke Ogre");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Ogre).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void OnakkeOgre_ManaValue_IsThree()
    {
        var c = OnakkeOgreFactory.Create(_alice);

        c.ManaCost.Should().Be("{2}{R}",
            "mana value 3: two generic pips plus one Red pip");
    }

    [Fact]
    public void OnakkeOgre_Colors_ContainsRedOnly()
    {
        var c = OnakkeOgreFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Red, "Onakke Ogre costs {2}{R}");
        colors.Should().HaveCount(1, "Onakke Ogre is exactly Red");
    }

    [Fact]
    public void OnakkeOgre_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Onakke Ogre", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Onakke Ogre");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Ogre).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    [Fact]
    public void OnakkeOgre_IsVanilla_NoAbilities()
    {
        var c = OnakkeOgreFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Onakke Ogre is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Onakke Ogre has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Onakke Ogre has no activated abilities");
    }
}
