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

    // -------------------------------------------------------------------
    // Mana Confluence — "{T}, Pay 1 life: Add one mana of any color."
    // Pay-life ANY-colour variant of the Horizon Canopy pay-life pattern.
    // Binds five pay-life ManaAbility instances (one per WUBRG); each
    // activation costs 1 life (CR 120.6 "Pay N life"). Life-floor gate
    // CR 119.4 — payable at exactly 1 life (drops to 0).
    // -------------------------------------------------------------------

    [Fact]
    public void ManaConfluence_PayLifeAnyColor_BindsFiveColours()
    {
        var land = new Land("Mana Confluence") { Owner = _alice, Controller = _alice };
        var entity = new CardEntity
        {
            Name = "Mana Confluence",
            TypeLine = "Land",
            OracleText = "{T}, Pay 1 life: Add one mana of any color.",
        };

        OracleManaBinder.Bind(land, entity, _alice);

        land.Abilities.OfType<IManaAbility>()
            .Select(m => m.ManaGenerated.ToString())
            .Should().BeEquivalentTo(
                new[] { "W", "U", "B", "R", "G" }.Select(c => ManaCost.Parse(c).ToString()),
                because: "any-colour pay-life mana fans out into one ability per WUBRG");
    }

    [Fact]
    public void ManaConfluence_PayLifeAnyColor_PaysLifeAndTaps()
    {
        var land = new Land("Mana Confluence") { Owner = _alice, Controller = _alice };
        var entity = new CardEntity
        {
            Name = "Mana Confluence",
            TypeLine = "Land",
            OracleText = "{T}, Pay 1 life: Add one mana of any color.",
        };

        OracleManaBinder.Bind(land, entity, _alice);

        var ability = land.Abilities.OfType<IManaAbility>().First();
        ability.Activate();
        _alice.LifeTotal.Should().Be(19, because: "the pay-life cost loses 1 life");
        land.IsTapped.Should().BeTrue(because: "the {T} cost taps the land");
    }

    [Fact]
    public void ManaConfluence_PayLifeAnyColor_LegalAtExactlyOneLife()
    {
        var dying = new Player("Dying", 1);
        var land = new Land("Mana Confluence") { Owner = dying, Controller = dying };
        var entity = new CardEntity
        {
            Name = "Mana Confluence",
            TypeLine = "Land",
            OracleText = "{T}, Pay 1 life: Add one mana of any color.",
        };

        OracleManaBinder.Bind(land, entity, dying);

        // CR 119.4 — "Pay 1 life" is payable at 1 life (drops to 0). Unlike the
        // stricter HorizonLandBinder gate, Mana Confluence reads the precise CR.
        land.Abilities.OfType<IManaAbility>()
            .Should().OnlyContain(m => m.CanActivate() == true);
    }

    // -------------------------------------------------------------------
    // Gemstone Mine — "{T}, Remove a mining counter from this land: Add one
    // mana of any color. If there are no mining counters on this land,
    // sacrifice it." Counter-cost any-colour mana + ETB three mining
    // counters + sac-when-empty.
    // -------------------------------------------------------------------

    [Fact]
    public void GemstoneMine_BindsFiveCounterManaAbilities_NotFreeAnyColor()
    {
        var land = new Land("Gemstone Mine") { Owner = _alice, Controller = _alice };
        land.SetZone(Majik.Core.Zones.ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);
        land.Counters.Add(Majik.Core.Counters.CounterType.Mining, 3);
        var entity = GemstoneEntity();

        OracleManaBinder.Bind(land, entity, _alice);

        var mana = land.Abilities.OfType<IManaAbility>().ToList();
        mana.Select(m => m.ManaGenerated.ToString())
            .Should().BeEquivalentTo(
                new[] { "W", "U", "B", "R", "G" }.Select(c => ManaCost.Parse(c).ToString()),
                because: "exactly five counter-cost any-colour abilities — NOT an extra "
                       + "free bare-{T} any-colour ability from the second sentence");
    }

    [Fact]
    public void GemstoneMine_ManaAbility_RemovesOneCounterPerActivation()
    {
        var land = new Land("Gemstone Mine") { Owner = _alice, Controller = _alice };
        land.SetZone(Majik.Core.Zones.ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);
        land.Counters.Add(Majik.Core.Counters.CounterType.Mining, 3);
        OracleManaBinder.Bind(land, GemstoneEntity(), _alice);

        var ability = land.Abilities.OfType<IManaAbility>().First();
        ability.Activate();

        land.Counters.Count(Majik.Core.Counters.CounterType.Mining)
            .Should().Be(2, because: "each activation removes one mining counter");
    }

    [Fact]
    public void GemstoneMine_RequiresAMiningCounter_ToActivate()
    {
        var land = new Land("Gemstone Mine") { Owner = _alice, Controller = _alice };
        land.SetZone(Majik.Core.Zones.ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);
        // No mining counters.
        OracleManaBinder.Bind(land, GemstoneEntity(), _alice);

        land.Abilities.OfType<IManaAbility>()
            .Should().OnlyContain(m => m.CanActivate() == false,
                because: "removing a mining counter is part of the cost (CR 119.4)");
    }

    [Fact]
    public void GemstoneMine_SacrificesItself_WhenLastCounterRemoved()
    {
        var land = new Land("Gemstone Mine") { Owner = _alice, Controller = _alice };
        land.SetZone(Majik.Core.Zones.ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);
        land.Counters.Add(Majik.Core.Counters.CounterType.Mining, 1);
        OracleManaBinder.Bind(land, GemstoneEntity(), _alice);

        var ability = land.Abilities.OfType<IManaAbility>().First();
        ability.Activate();

        // "If there are no mining counters on this land, sacrifice it." (CR 701.16)
        land.Zone.Should().Be(Majik.Core.Zones.ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);
    }

    [Fact]
    public void GemstoneMine_EntersWithThreeMiningCounters_ViaEtbTrigger()
    {
        var bus = new Majik.Core.Events.EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new Majik.Core.Abilities.TriggerManager(stack, bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var land = new Land("Gemstone Mine") { Owner = _alice, Controller = _alice };
        land.SetZone(Majik.Core.Zones.ZoneType.Hand);
        _alice.Zones.Hand.AddCard(land);

        OracleManaBinder.Bind(land, GemstoneEntity(), _alice);
        triggers.BindCard(land);

        zones.MoveCardTo(land, Majik.Core.Zones.ZoneType.Battlefield, controller: _alice);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        land.Counters.Count(Majik.Core.Counters.CounterType.Mining)
            .Should().Be(3, because: "Gemstone Mine enters with three mining counters");
    }

    private static CardEntity GemstoneEntity() => new()
    {
        Name = "Gemstone Mine",
        TypeLine = "Land",
        OracleText = "This land enters with three mining counters on it.\n"
                   + "{T}, Remove a mining counter from this land: Add one mana of any color. "
                   + "If there are no mining counters on this land, sacrifice it.",
    };
}
