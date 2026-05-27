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
/// Unit tests for <see cref="GoblinRoughriderFactory"/>.
///
/// Card: Goblin Roughrider — Creature — Goblin Knight {2}{R} 3/2 (Magic 2010 /
/// Modern reprints). Vanilla — no printed keywords, triggers, statics, or
/// activated abilities.
/// </summary>
public class GoblinRoughriderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GoblinRoughrider_Identity()
    {
        var c = GoblinRoughriderFactory.Create(_alice);

        c.Name.Should().Be("Goblin Roughrider");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GoblinRoughrider_ManaValue_IsThree()
    {
        var c = GoblinRoughriderFactory.Create(_alice);

        c.ManaCost.Should().Be("{2}{R}",
            "mana value 3: two generic pips plus one Red pip");
    }

    [Fact]
    public void GoblinRoughrider_Colors_ContainsRedOnly()
    {
        var c = GoblinRoughriderFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Red, "Goblin Roughrider costs {2}{R}");
        colors.Should().HaveCount(1, "Goblin Roughrider is exactly Red");
    }

    [Fact]
    public void GoblinRoughrider_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Goblin Roughrider", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Goblin Roughrider");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
    }

    [Fact]
    public void GoblinRoughrider_IsVanilla_NoAbilities()
    {
        var c = GoblinRoughriderFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Goblin Roughrider is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Goblin Roughrider has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Goblin Roughrider has no activated abilities");
    }
}
