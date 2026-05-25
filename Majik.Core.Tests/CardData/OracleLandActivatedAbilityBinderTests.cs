using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>Unit tests for <see cref="OracleLandActivatedAbilityBinder"/>.</summary>
public class OracleLandActivatedAbilityBinderTests
{
    private readonly Player _alice = new("Alice", 20);

    // -------------------------------------------------------------------
    // Happy-path binding tests
    // -------------------------------------------------------------------

    [Fact]
    public void Bind_FetchLand_AttachesOneActivatedAbility()
    {
        var fetch = new Land("Misty Rainforest") { Owner = _alice, Controller = _alice };
        var entity = MistyRainforestEntity();

        var bound = OracleLandActivatedAbilityBinder.Bind(fetch, entity, _alice);

        bound.Should().BeTrue();
        fetch.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Bind_FetchLand_AbilityHasThreeCosts_TapPayLifeSacrifice()
    {
        var fetch = new Land("Misty Rainforest") { Owner = _alice, Controller = _alice };

        OracleLandActivatedAbilityBinder.Bind(fetch, MistyRainforestEntity(), _alice);

        var ab = fetch.Abilities.OfType<ActivatedAbility>().Single();
        ab.Costs.Should().HaveCount(3);

        var additionalCosts = ab.Costs.OfType<AdditionalCost>().ToList();
        additionalCosts.Should().ContainSingle(a => a.CostType == AdditionalCostType.Tap,
            because: "fetch land taps as part of the cost");
        additionalCosts.Should().ContainSingle(a => a.CostType == AdditionalCostType.PayLife,
            because: "fetch land requires paying 1 life");
        additionalCosts.Should().ContainSingle(a => a.CostType == AdditionalCostType.Sacrifice,
            because: "fetch land sacrifices itself");
    }

    [Fact]
    public void Bind_FetchLand_AbilityHasOneEffect()
    {
        var fetch = new Land("Misty Rainforest") { Owner = _alice, Controller = _alice };

        OracleLandActivatedAbilityBinder.Bind(fetch, MistyRainforestEntity(), _alice);

        var ab = fetch.Abilities.OfType<ActivatedAbility>().Single();
        ab.Effects.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("Forest", "Island",   "Misty Rainforest")]
    [InlineData("Island", "Swamp",    "Polluted Delta")]
    [InlineData("Swamp",  "Mountain", "Bloodstained Mire")]
    [InlineData("Mountain","Forest",  "Wooded Foothills")]
    [InlineData("Forest", "Plains",   "Windswept Heath")]
    [InlineData("Plains", "Island",   "Flooded Strand")]
    [InlineData("Island", "Mountain", "Scalding Tarn")]
    [InlineData("Swamp",  "Forest",   "Verdant Catacombs")]
    [InlineData("Swamp",  "Plains",   "Marsh Flats")]
    [InlineData("Mountain","Plains",  "Arid Mesa")]
    public void Bind_AllFetchLandVariants_EachAttachesAbility(
        string subtypeA, string subtypeB, string cardName)
    {
        var fetch = new Land(cardName) { Owner = _alice, Controller = _alice };
        var entity = new CardEntity
        {
            Name = cardName,
            TypeLine = "Land",
            OracleText = $"{{T}}, Pay 1 life, Sacrifice {cardName}: Search your library for a " +
                         $"{subtypeA} or {subtypeB} card, put it onto the battlefield, then shuffle.",
        };

        var bound = OracleLandActivatedAbilityBinder.Bind(fetch, entity, _alice);

        bound.Should().BeTrue();
        fetch.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------------
    // Effect execution — library search + zone move
    // -------------------------------------------------------------------

    [Fact]
    public void Effect_MovesMatchingBasicFromLibraryToBattlefield()
    {
        var fetch = new Land("Misty Rainforest") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(fetch);

        // Put a Forest in the library.
        var forest = new Land("Forest",
            subtypes: new[] { CardSubtype.Forest }) { Owner = _alice, Controller = _alice };
        _alice.Zones.Library.AddCard(forest);

        OracleLandActivatedAbilityBinder.Bind(fetch, MistyRainforestEntity(), _alice);

        var ab = fetch.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.Zones.Library.GetCards().Should().NotContain(forest,
            because: "the forest was searched out of the library");
        _alice.Zones.Battlefield.GetCards().Should().Contain(forest,
            because: "the forest entered the battlefield via the fetch effect");
        forest.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Effect_EmptyLibrary_DoesNotThrow()
    {
        var fetch = new Land("Misty Rainforest") { Owner = _alice, Controller = _alice };
        OracleLandActivatedAbilityBinder.Bind(fetch, MistyRainforestEntity(), _alice);

        var ab = fetch.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => ab.Resolve();
        act.Should().NotThrow(because: "no match in library is a legal fizzle");
    }

    [Fact]
    public void Effect_NoMatchingSubtype_DoesNotMoveLand()
    {
        var fetch = new Land("Misty Rainforest") { Owner = _alice, Controller = _alice };

        // Library has a Mountain — not a Forest or Island.
        var mountain = new Land("Mountain",
            subtypes: new[] { CardSubtype.Mountain }) { Owner = _alice, Controller = _alice };
        _alice.Zones.Library.AddCard(mountain);

        OracleLandActivatedAbilityBinder.Bind(fetch, MistyRainforestEntity(), _alice);

        var ab = fetch.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.Zones.Library.GetCards().Should().Contain(mountain,
            because: "Mountain doesn't match Forest/Island — it stays in the library");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(mountain);
    }

    // -------------------------------------------------------------------
    // Non-fetch / non-land cards — nothing attached
    // -------------------------------------------------------------------

    [Fact]
    public void Bind_BasicLand_NoFetchText_ReturnsFalse()
    {
        var forest = new Land("Forest") { Owner = _alice, Controller = _alice };
        var entity = new CardEntity { Name = "Forest", TypeLine = "Basic Land — Forest", OracleText = null };

        var bound = OracleLandActivatedAbilityBinder.Bind(forest, entity, _alice);

        bound.Should().BeFalse();
        forest.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void Bind_NonLandCard_IgnoredEvenWithFetchOracleText()
    {
        var creature = new Creature("Fake Fetch", "", 1, 1) { Owner = _alice, Controller = _alice };
        var entity = new CardEntity
        {
            Name = "Fake Fetch",
            TypeLine = "Creature",
            OracleText = "{T}, Pay 1 life, Sacrifice Fake Fetch: Search your library for a Forest or Island card, " +
                         "put it onto the battlefield, then shuffle.",
        };

        var bound = OracleLandActivatedAbilityBinder.Bind(creature, entity, _alice);

        bound.Should().BeFalse();
        creature.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void Bind_NullOracleText_ReturnsFalse()
    {
        var land = new Land("Test Land") { Owner = _alice, Controller = _alice };
        var entity = new CardEntity { Name = "Test Land", TypeLine = "Land", OracleText = null };

        var bound = OracleLandActivatedAbilityBinder.Bind(land, entity, _alice);

        bound.Should().BeFalse();
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static CardEntity MistyRainforestEntity() => new()
    {
        Name = "Misty Rainforest",
        TypeLine = "Land",
        OracleText = "{T}, Pay 1 life, Sacrifice Misty Rainforest: Search your library for a Forest or Island card, " +
                     "put it onto the battlefield, then shuffle.",
    };
}
