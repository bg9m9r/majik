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
/// Unit tests for <see cref="ScatheZombiesFactory"/>.
///
/// Card: Scathe Zombies — Creature — Zombie {2}{B} 2/2 (Alpha / Revised /
/// Modern reprints). Vanilla — no printed keywords, triggers, statics, or
/// activated abilities.
/// </summary>
[Trait("Color", "B")]
public class ScatheZombiesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ScatheZombies_Identity()
    {
        var c = ScatheZombiesFactory.Create(_alice);

        c.Name.Should().Be("Scathe Zombies");
        c.ManaCost.Should().Be("{2}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ScatheZombies_ManaValue_IsThree()
    {
        var c = ScatheZombiesFactory.Create(_alice);

        c.ManaCost.Should().Be("{2}{B}",
            "mana value 3: two generic pips plus one Black pip");
    }

    [Fact]
    public void ScatheZombies_Colors_ContainsBlackOnly()
    {
        var c = ScatheZombiesFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Black, "Scathe Zombies costs {2}{B}");
        colors.Should().HaveCount(1, "Scathe Zombies is exactly Black");
    }
    [Fact]
    public void ScatheZombies_IsVanilla_NoAbilities()
    {
        var c = ScatheZombiesFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Scathe Zombies is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Scathe Zombies has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Scathe Zombies has no activated abilities");
    }
}
