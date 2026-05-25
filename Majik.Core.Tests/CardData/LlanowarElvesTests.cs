using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>Unit tests for <see cref="LlanowarElvesFactory"/>.</summary>
public class LlanowarElvesTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void LlanowarElves_NameIsCorrect()
    {
        var elf = LlanowarElvesFactory.Create(_alice);
        elf.Name.Should().Be("Llanowar Elves");
    }

    [Fact]
    public void LlanowarElves_IsCreature()
    {
        var elf = LlanowarElvesFactory.Create(_alice);
        elf.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void LlanowarElves_HasElfDruidSubtypes()
    {
        var elf = LlanowarElvesFactory.Create(_alice);
        elf.HasSubtype(CardSubtype.Elf).Should().BeTrue("Llanowar Elves is an Elf");
        elf.HasSubtype(CardSubtype.Druid).Should().BeTrue("Llanowar Elves is a Druid");
    }

    [Fact]
    public void LlanowarElves_PowerAndToughnessAreOneOne()
    {
        var elf = LlanowarElvesFactory.Create(_alice);
        elf.Power.Should().Be(1);
        elf.Toughness.Should().Be(1);
    }

    [Fact]
    public void LlanowarElves_ManaCostIsGreen()
    {
        var elf = LlanowarElvesFactory.Create(_alice);
        elf.ManaCost.Should().Be("{G}");
    }

    [Fact]
    public void LlanowarElves_OwnerAndControllerAreSet()
    {
        var elf = LlanowarElvesFactory.Create(_alice);
        elf.Owner.Should().BeSameAs(_alice);
        elf.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LlanowarElves_HasOneGreenManaAbility()
    {
        var elf = LlanowarElvesFactory.Create(_alice);
        var mas = elf.Abilities.OfType<ManaAbility>().ToList();
        mas.Should().HaveCount(1, "a single green mana ability");
    }

    [Fact]
    public void LlanowarElves_HasNoKeywordAbilities()
    {
        var elf = LlanowarElvesFactory.Create(_alice);
        elf.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Llanowar Elves is vanilla — no keyword abilities");
    }
}
