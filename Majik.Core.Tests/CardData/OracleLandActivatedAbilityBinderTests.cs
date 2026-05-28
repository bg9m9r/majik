using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Moq;
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
    // Agent prompt — CR 701.19a "the player chooses which card to fetch"
    // -------------------------------------------------------------------

    [Fact]
    public void Effect_ConsultsRegisteredAgent_WhenChoosingAmongCandidates()
    {
        // Production bug surfaced in the live fetchland test (PR #1003 wired
        // AgentRegistry but the binder's FetchEffect never consulted it):
        // the human user saw their fetchland resolve without ever being asked
        // which land to fetch. The binder must mirror FetchLandCycleFactory
        // and call agent.ChooseLibraryPickAsync when an agent is registered
        // for the fetchland's controller.
        var alice = new Player("Alice", 20);
        AgentRegistry.Clear();
        try
        {
            var fetch = new Land("Misty Rainforest") { Owner = alice, Controller = alice };
            alice.Zones.Battlefield.AddCard(fetch);

            var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest })
            {
                Owner = alice, Controller = alice,
            };
            var island = new Land("Island", subtypes: new[] { CardSubtype.Island })
            {
                Owner = alice, Controller = alice,
            };
            alice.Zones.Library.AddCard(forest);
            alice.Zones.Library.AddCard(island);

            // Mock agent always picks the Island — proves the binder
            // honoured the agent's choice rather than falling through to
            // FirstOrDefault (which would have picked the Forest, registered
            // first in the library).
            var agent = new Mock<IPlayerAgent>();
            agent.Setup(a => a.ChooseLibraryPickAsync(
                    It.IsAny<Majik.Core.Game.GameContext?>(),
                    It.IsAny<IReadOnlyList<ICard>>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ICard?)island);
            AgentRegistry.Set(alice, agent.Object);

            OracleLandActivatedAbilityBinder.Bind(fetch, MistyRainforestEntity(), alice);
            fetch.Abilities.OfType<ActivatedAbility>().Single().Resolve();

            alice.Zones.Battlefield.GetCards().Should().Contain(island,
                because: "the agent picked the Island, not FirstOrDefault");
            alice.Zones.Battlefield.GetCards().Should().NotContain(forest);
            alice.Zones.Library.GetCards().Should().Contain(forest);
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    [Fact]
    public void Effect_AgentPicksNull_ModelsFindNothing()
    {
        // CR 701.19a — a player may decline to choose a card from a
        // successful search. The binder must surface that as "nothing
        // moved to the battlefield", not as a thrown / silent
        // FirstOrDefault.
        var alice = new Player("Alice", 20);
        AgentRegistry.Clear();
        try
        {
            var fetch = new Land("Misty Rainforest") { Owner = alice, Controller = alice };
            alice.Zones.Battlefield.AddCard(fetch);
            var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest })
            {
                Owner = alice, Controller = alice,
            };
            alice.Zones.Library.AddCard(forest);

            var agent = new Mock<IPlayerAgent>();
            agent.Setup(a => a.ChooseLibraryPickAsync(
                    It.IsAny<Majik.Core.Game.GameContext?>(),
                    It.IsAny<IReadOnlyList<ICard>>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ICard?)null);
            AgentRegistry.Set(alice, agent.Object);

            OracleLandActivatedAbilityBinder.Bind(fetch, MistyRainforestEntity(), alice);
            fetch.Abilities.OfType<ActivatedAbility>().Single().Resolve();

            alice.Zones.Library.GetCards().Should().Contain(forest,
                because: "the agent declined the search; Forest stays in library");
            alice.Zones.Battlefield.GetCards().Should().NotContain(forest);
        }
        finally
        {
            AgentRegistry.Clear();
        }
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
