using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
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
    // "an" article fetchlands (vowel-leading first basic) — CR 701.19a.
    // Polluted Delta / Scalding Tarn are worded "Search your library for an
    // Island or ...". The regex must accept both "a" and "an".
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("Polluted Delta", "Island", "Swamp")]
    [InlineData("Scalding Tarn", "Island", "Mountain")]
    public void Bind_AnArticleFetchLand_AttachesFetchAbility(
        string cardName, string subtypeA, string subtypeB)
    {
        var fetch = new Land(cardName) { Owner = _alice, Controller = _alice };
        var entity = new CardEntity
        {
            Name = cardName,
            TypeLine = "Land",
            // Real seed wording: "an Island or ..." with "Sacrifice this land".
            OracleText = $"{{T}}, Pay 1 life, Sacrifice this land: Search your library for an " +
                         $"{subtypeA} or {subtypeB} card, put it onto the battlefield, then shuffle.",
        };

        var bound = OracleLandActivatedAbilityBinder.Bind(fetch, entity, _alice);

        bound.Should().BeTrue();
        var ab = fetch.Abilities.OfType<ActivatedAbility>().Single();
        ab.Costs.Should().HaveCount(3);
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap);
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(c => c.CostType == AdditionalCostType.PayLife);
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice);
        ab.Effects.Should().HaveCount(1);
    }

    [Fact]
    public void Bind_AnArticleFetchLand_EffectMovesMatchingBasic()
    {
        var fetch = new Land("Polluted Delta") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(fetch);

        var swamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp })
        {
            Owner = _alice, Controller = _alice,
        };
        _alice.Zones.Library.AddCard(swamp);

        var entity = new CardEntity
        {
            Name = "Polluted Delta",
            TypeLine = "Land",
            OracleText = "{T}, Pay 1 life, Sacrifice this land: Search your library for an Island or Swamp card, " +
                         "put it onto the battlefield, then shuffle.",
        };

        OracleLandActivatedAbilityBinder.Bind(fetch, entity, _alice);
        fetch.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        _alice.Zones.Library.GetCards().Should().NotContain(swamp);
        _alice.Zones.Battlefield.GetCards().Should().Contain(swamp);
        swamp.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -------------------------------------------------------------------
    // Prismatic Vista — "Search your library for a basic land card"
    // (one "basic land card", not two named basics). CR 205.4a.
    // -------------------------------------------------------------------

    [Fact]
    public void Bind_PrismaticVista_AttachesFetchAbility_WithThreeCosts()
    {
        var fetch = new Land("Prismatic Vista") { Owner = _alice, Controller = _alice };

        var bound = OracleLandActivatedAbilityBinder.Bind(fetch, PrismaticVistaEntity(), _alice);

        bound.Should().BeTrue();
        var ab = fetch.Abilities.OfType<ActivatedAbility>().Single();
        ab.Costs.Should().HaveCount(3);
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap);
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(c => c.CostType == AdditionalCostType.PayLife);
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice);
        ab.Effects.Should().HaveCount(1);
    }

    [Fact]
    public void Bind_PrismaticVista_EffectFetchesAnyBasicLand()
    {
        var fetch = new Land("Prismatic Vista") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(fetch);

        // A basic Forest (Basic supertype + Forest subtype).
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest })
        {
            Owner = _alice, Controller = _alice,
        };
        _alice.Zones.Library.AddCard(forest);

        OracleLandActivatedAbilityBinder.Bind(fetch, PrismaticVistaEntity(), _alice);
        fetch.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        _alice.Zones.Library.GetCards().Should().NotContain(forest);
        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        forest.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Bind_PrismaticVista_DoesNotFetchNonBasicLand()
    {
        var fetch = new Land("Prismatic Vista") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(fetch);

        // A nonbasic dual land with Forest subtype but NO Basic supertype.
        var dual = new Land("Stomping Ground",
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Forest })
        {
            Owner = _alice, Controller = _alice,
        };
        _alice.Zones.Library.AddCard(dual);

        OracleLandActivatedAbilityBinder.Bind(fetch, PrismaticVistaEntity(), _alice);
        fetch.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        _alice.Zones.Library.GetCards().Should().Contain(dual,
            because: "Prismatic Vista only fetches BASIC lands (CR 205.4a)");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(dual);
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
    // Horizon Canopy cycle — "{1}, {T}, Sacrifice this land: Draw a card."
    // The sac-to-draw activated ability (Fiery Islet, Sunbaked Canyon, etc.).
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("Fiery Islet")]
    [InlineData("Sunbaked Canyon")]
    public void Bind_HorizonLandSacDraw_AttachesActivatedAbility(string name)
    {
        var land = new Land(name) { Owner = _alice, Controller = _alice };
        var entity = HorizonLandEntity(name);

        var bound = OracleLandActivatedAbilityBinder.Bind(land, entity, _alice);

        bound.Should().BeTrue();
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Bind_HorizonLandSacDraw_AbilityHasManaTapAndSacrificeCosts()
    {
        var land = new Land("Fiery Islet") { Owner = _alice, Controller = _alice };

        OracleLandActivatedAbilityBinder.Bind(land, HorizonLandEntity("Fiery Islet"), _alice);

        var ab = land.Abilities.OfType<ActivatedAbility>().Single();
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
            because: "the sac-draw ability taps the land");
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
            because: "the sac-draw ability sacrifices the land");
        ab.Costs.OfType<ManaCostCost>().Should().ContainSingle(c => c.Cost.ToString() == ManaCost.Parse("1").ToString(),
            because: "the sac-draw ability has a {1} generic mana cost");
        ab.Effects.Should().HaveCount(1);
    }

    [Fact]
    public void Bind_HorizonLandSacDraw_EffectDrawsTopCard()
    {
        var land = new Land("Fiery Islet") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(land);

        var top = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain })
        {
            Owner = _alice, Controller = _alice,
        };
        _alice.Zones.Library.AddCard(top);

        OracleLandActivatedAbilityBinder.Bind(land, HorizonLandEntity("Fiery Islet"), _alice);
        land.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top,
            because: "the sac-draw effect draws the top card of the library");
        _alice.Zones.Library.GetCards().Should().NotContain(top);
    }

    // -------------------------------------------------------------------
    // Sac-fetch basic onto the battlefield TAPPED — Evolving Wilds,
    // Terramorphic Expanse, Fabled Passage. Cost is {T} + Sacrifice only
    // (NO "Pay 1 life", unlike the fetchland / Prismatic Vista cycle).
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("Evolving Wilds")]
    [InlineData("Terramorphic Expanse")]
    public void Bind_SacFetchTappedLand_AttachesAbility_TapAndSacrificeOnly_NoPayLife(string name)
    {
        var land = new Land(name) { Owner = _alice, Controller = _alice };

        var bound = OracleLandActivatedAbilityBinder.Bind(land, SacFetchTappedEntity(name), _alice);

        bound.Should().BeTrue();
        var ab = land.Abilities.OfType<ActivatedAbility>().Single();
        ab.Costs.Should().HaveCount(2, because: "the cost is {T} + Sacrifice with no Pay 1 life");
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap);
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice);
        ab.Costs.OfType<AdditionalCost>().Should().NotContain(c => c.CostType == AdditionalCostType.PayLife);
        ab.Effects.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("Evolving Wilds")]
    [InlineData("Terramorphic Expanse")]
    public void Bind_SacFetchTappedLand_EffectFetchesBasicTapped_AndSacrificesSelf_AndShuffles(string name)
    {
        var land = new Land(name) { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(land);

        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest })
        {
            Owner = _alice, Controller = _alice,
        };
        _alice.Zones.Library.AddCard(forest);

        OracleLandActivatedAbilityBinder.Bind(land, SacFetchTappedEntity(name), _alice);
        land.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        // Fetched basic moved to battlefield, tapped.
        _alice.Zones.Library.GetCards().Should().NotContain(forest);
        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        forest.Zone.Should().Be(ZoneType.Battlefield);
        forest.IsTapped.Should().BeTrue(because: "the basic enters the battlefield tapped");

        // Self-sacrifice — the sac-fetch land left the battlefield to its
        // owner's graveyard.
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        land.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Bind_SacFetchTappedLand_OnlyFetchesBasicLands()
    {
        var land = new Land("Evolving Wilds") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(land);

        var dual = new Land("Stomping Ground",
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Forest })
        {
            Owner = _alice, Controller = _alice,
        };
        _alice.Zones.Library.AddCard(dual);

        OracleLandActivatedAbilityBinder.Bind(land, SacFetchTappedEntity("Evolving Wilds"), _alice);
        land.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        _alice.Zones.Library.GetCards().Should().Contain(dual,
            because: "only BASIC lands are fetchable (CR 205.4a)");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(dual);
    }

    [Fact]
    public void Bind_FabledPassage_AttachesAbility_TapAndSacrificeOnly()
    {
        var land = new Land("Fabled Passage") { Owner = _alice, Controller = _alice };

        var bound = OracleLandActivatedAbilityBinder.Bind(land, FabledPassageEntity(), _alice);

        bound.Should().BeTrue();
        var ab = land.Abilities.OfType<ActivatedAbility>().Single();
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap);
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice);
        ab.Costs.OfType<AdditionalCost>().Should().NotContain(c => c.CostType == AdditionalCostType.PayLife);
        ab.Effects.Should().HaveCount(1);
    }

    [Fact]
    public void Bind_FabledPassage_UntapsFetchedLand_WhenControllerControlsFourOrMoreLands()
    {
        var land = new Land("Fabled Passage") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(land);

        // Three other lands already on the battlefield; Fabled Passage itself is
        // sacrificed (does not count), the fetched land is the 4th -> untaps.
        for (var i = 0; i < 3; i++)
        {
            _alice.Zones.Battlefield.AddCard(
                new Land("Plains", supertypes: new[] { CardSupertype.Basic }, subtypes: new[] { CardSubtype.Plains })
                {
                    Owner = _alice, Controller = _alice,
                });
        }

        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic }, subtypes: new[] { CardSubtype.Forest })
        {
            Owner = _alice, Controller = _alice,
        };
        _alice.Zones.Library.AddCard(forest);

        OracleLandActivatedAbilityBinder.Bind(land, FabledPassageEntity(), _alice);
        land.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        forest.IsTapped.Should().BeFalse(
            because: "you control four or more lands, so the fetched land is untapped");
    }

    [Fact]
    public void Bind_FabledPassage_LeavesFetchedLandTapped_WhenControllerControlsFewerThanFourLands()
    {
        var land = new Land("Fabled Passage") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(land);

        // No other lands. Fabled Passage is sacrificed; the fetched land is the
        // only land -> 1 < 4 -> stays tapped.
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic }, subtypes: new[] { CardSubtype.Forest })
        {
            Owner = _alice, Controller = _alice,
        };
        _alice.Zones.Library.AddCard(forest);

        OracleLandActivatedAbilityBinder.Bind(land, FabledPassageEntity(), _alice);
        land.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        forest.IsTapped.Should().BeTrue(
            because: "fewer than four lands -> the fetched land stays tapped");
    }

    [Fact]
    public void Bind_SacFetchTappedLand_EmptyLibrary_DoesNotThrow()
    {
        var land = new Land("Evolving Wilds") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(land);
        OracleLandActivatedAbilityBinder.Bind(land, SacFetchTappedEntity("Evolving Wilds"), _alice);

        var ab = land.Abilities.OfType<ActivatedAbility>().Single();
        var act = () => ab.Resolve();
        act.Should().NotThrow(because: "no basic in library is a legal fizzle");
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static CardEntity SacFetchTappedEntity(string name) => new()
    {
        Name = name,
        TypeLine = "Land",
        // Real seed wording (verified via EmbeddedCardRepository).
        OracleText = "{T}, Sacrifice this land: Search your library for a basic land card, " +
                     "put it onto the battlefield tapped, then shuffle.",
    };

    private static CardEntity FabledPassageEntity() => new()
    {
        Name = "Fabled Passage",
        TypeLine = "Land",
        // Real seed wording (verified via EmbeddedCardRepository).
        OracleText = "{T}, Sacrifice this land: Search your library for a basic land card, " +
                     "put it onto the battlefield tapped, then shuffle. " +
                     "Then if you control four or more lands, untap that land.",
    };

    private static CardEntity HorizonLandEntity(string name) => new()
    {
        Name = name,
        TypeLine = "Land",
        // Real seed wording (verified via EmbeddedCardRepository): the pain-mana
        // line is bound by OracleManaBinder; this binder only handles the
        // sac-to-draw activated ability.
        OracleText = "{T}, Pay 1 life: Add {U} or {R}.\n" +
                     "{1}, {T}, Sacrifice this land: Draw a card.",
    };

    private static CardEntity MistyRainforestEntity() => new()
    {
        Name = "Misty Rainforest",
        TypeLine = "Land",
        OracleText = "{T}, Pay 1 life, Sacrifice Misty Rainforest: Search your library for a Forest or Island card, " +
                     "put it onto the battlefield, then shuffle.",
    };

    private static CardEntity PrismaticVistaEntity() => new()
    {
        Name = "Prismatic Vista",
        TypeLine = "Land",
        // Real seed wording (verified via EmbeddedCardRepository).
        OracleText = "{T}, Pay 1 life, Sacrifice this land: Search your library for a basic land card, " +
                     "put it onto the battlefield, then shuffle.",
    };

}
