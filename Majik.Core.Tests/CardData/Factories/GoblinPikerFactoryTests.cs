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
/// Unit tests for <see cref="GoblinPikerFactory"/>.
///
/// Card: Goblin Piker — Creature — Goblin Warrior {1}{R} 2/1 (Magic 2010 /
/// Modern reprints). Vanilla — no printed keywords, triggers, statics, or
/// activated abilities.
/// </summary>
public class GoblinPikerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GoblinPiker_Identity()
    {
        var c = GoblinPikerFactory.Create(_alice);

        c.Name.Should().Be("Goblin Piker");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GoblinPiker_ManaValue_IsTwo()
    {
        var c = GoblinPikerFactory.Create(_alice);

        c.ManaCost.Should().Be("{1}{R}",
            "mana value 2: one generic pip plus one Red pip");
    }

    [Fact]
    public void GoblinPiker_Colors_ContainsRedOnly()
    {
        var c = GoblinPikerFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Red, "Goblin Piker costs {1}{R}");
        colors.Should().HaveCount(1, "Goblin Piker is exactly Red");
    }

    [Fact]
    public void GoblinPiker_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Goblin Piker", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Goblin Piker");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    [Fact]
    public void GoblinPiker_IsVanilla_NoAbilities()
    {
        var c = GoblinPikerFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Goblin Piker is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Goblin Piker has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Goblin Piker has no activated abilities");
    }
}
