using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
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

    // -------------------------------------------------------------------
    // Horizon Canopy cycle — "{T}, Pay 1 life: Add {A} or {B}." pain mana.
    // The cost prefix isn't a bare {T}, so the binder must recognise the
    // "Pay 1 life: Add" shape and split the dual into two pay-life mana
    // abilities (one per colour) via HorizonLandBinder.
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("Fiery Islet", "U", "R")]
    [InlineData("Sunbaked Canyon", "R", "W")]
    public void HorizonLand_PayLifeDual_BindsBothColours(string name, string a, string b)
    {
        var land = new Land(name) { Owner = _alice, Controller = _alice };
        var entity = new CardEntity
        {
            Name = name,
            TypeLine = "Land",
            OracleText = $"{{T}}, Pay 1 life: Add {{{a}}} or {{{b}}}.\n"
                       + "{1}, {T}, Sacrifice this land: Draw a card.",
        };

        OracleManaBinder.Bind(land, entity, _alice);

        // Each colour is a separate ManaAbility; activating one taps the land,
        // so assert the produced colour via ManaGenerated (set at construction
        // for the pay-life shape) rather than activating both on one land.
        var produced = land.Abilities.OfType<IManaAbility>()
            .Select(m => m.ManaGenerated.ToString())
            .ToList();
        produced.Should().BeEquivalentTo(
            new[] { ManaCost.Parse(a).ToString(), ManaCost.Parse(b).ToString() },
            because: "the dual pain-mana is split into one ManaAbility per colour");
    }

    [Fact]
    public void HorizonLand_PayLifeMana_RequiresLifeAboveOne()
    {
        var dying = new Player("Dying", 1);
        var land = new Land("Fiery Islet") { Owner = dying, Controller = dying };
        var entity = new CardEntity
        {
            Name = "Fiery Islet",
            TypeLine = "Land",
            OracleText = "{T}, Pay 1 life: Add {U} or {R}.",
        };

        OracleManaBinder.Bind(land, entity, dying);

        // CR 119.4 — can't pay 1 life when you only have 1.
        land.Abilities.OfType<IManaAbility>()
            .Should().OnlyContain(m => m.CanActivate() == false);
    }

    [Fact]
    public void HorizonLand_PayLifeMana_PaysLifeOnActivation()
    {
        var land = new Land("Fiery Islet") { Owner = _alice, Controller = _alice };
        var entity = new CardEntity
        {
            Name = "Fiery Islet",
            TypeLine = "Land",
            OracleText = "{T}, Pay 1 life: Add {U} or {R}.",
        };

        OracleManaBinder.Bind(land, entity, _alice);

        var ability = land.Abilities.OfType<IManaAbility>().First();
        ability.Activate();
        _alice.LifeTotal.Should().Be(19, because: "activating the pain mana costs 1 life");
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

        // CR 302.6 — clear summoning sickness so the {T} mana ability is
        // legal to activate; this test asserts the bound mana output, not
        // the sickness gate.
        card.ClearSummoningSickness();

        card.Abilities.OfType<IManaAbility>().Single()
            .Activate().Should().Be(ManaCost.Parse("G"));
    }
}
