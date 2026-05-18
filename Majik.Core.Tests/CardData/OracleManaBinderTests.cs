using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

public class OracleManaBinderTests
{
    private readonly Player _alice = new("Alice", 20);

    [Theory]
    [InlineData("Mountain", "Mountain", "R")]
    [InlineData("Island", "Island", "U")]
    [InlineData("Forest", "Forest", "G")]
    [InlineData("Plains", "Plains", "W")]
    [InlineData("Swamp", "Swamp", "B")]
    public void BasicLand_BySubtype_TapsForCorrectColor(string name, string subtype, string color)
    {
        var card = new Land(name, new[] { CardSupertype.Basic },
            new[] { Enum.Parse<CardSubtype>(subtype) });
        var entity = new CardEntity
        {
            Name = name, TypeLine = $"Basic Land — {subtype}",
            OracleText = $"({{T}}: Add {{{color}}}.)",
        };

        OracleManaBinder.Bind(card, entity, _alice);

        var mana = card.Abilities.OfType<IManaAbility>().Single();
        mana.Activate().Should().Be(ManaCost.Parse(color));
    }

    [Fact]
    public void SimpleTapForMana_FromOracleText()
    {
        var card = new Land("Custom Land");
        var entity = new CardEntity
        {
            Name = "Custom Land", TypeLine = "Land",
            OracleText = "{T}: Add {R}.",
        };

        OracleManaBinder.Bind(card, entity, _alice);

        card.Abilities.OfType<IManaAbility>().Single()
            .Activate().Should().Be(ManaCost.Parse("R"));
    }

    [Fact]
    public void NoManaText_NoAbility()
    {
        var card = new Creature("Bear", "1G", 2, 2);
        var entity = new CardEntity { Name = "Bear", OracleText = "" };

        OracleManaBinder.Bind(card, entity, _alice);

        card.Abilities.OfType<IManaAbility>().Should().BeEmpty();
    }

    [Fact]
    public void LlanowarElves_TextPattern_TapsForGreen()
    {
        var card = new Creature("Llanowar Elves", "G", 1, 1);
        var entity = new CardEntity
        {
            Name = "Llanowar Elves",
            TypeLine = "Creature — Elf Druid",
            OracleText = "{T}: Add {G}.",
        };

        OracleManaBinder.Bind(card, entity, _alice);

        card.Abilities.OfType<IManaAbility>().Single()
            .Activate().Should().Be(ManaCost.Parse("G"));
    }
}
