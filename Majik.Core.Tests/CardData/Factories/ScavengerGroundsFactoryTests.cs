using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ScavengerGroundsFactory"/>.
///
/// Scavenger Grounds — Land — Desert (Hour of Devastation).
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {2}, {T}, Sacrifice a Desert: Exile all graveyards."
///
/// Covers identity + dispatch, the {T}: Add {C} mana ability, and the
/// {2},{T},Sacrifice-a-Desert activated ability — whose "Sacrifice a Desert"
/// cost is the real <see cref="SacrificeFilteredCost"/> (CR 701.16) and whose
/// resolution exiles all reachable graveyards (CR 406.2).
/// </summary>
[Trait("Color", "C")]
public class ScavengerGroundsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ScavengerGrounds_IsLand_Desert_WithCorrectName()
    {
        var land = ScavengerGroundsFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Desert).Should().BeTrue("printed type is Land — Desert");
        land.Name.Should().Be("Scavenger Grounds");
    }

    [Fact]
    public void ScavengerGrounds_RoutesThroughDispatcher()
    {
        var land = (Land)NamedCardFactory.Create("Scavenger Grounds", _alice);
        land.Name.Should().Be("Scavenger Grounds");
    }

    [Fact]
    public void ScavengerGrounds_HasColourlessManaAbility()
    {
        var land = ScavengerGroundsFactory.Create(_alice);

        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        mana.ManaGenerated.Generic.Should().Be(1, "{T}: Add {C} (modeled as generic)");
    }

    [Fact]
    public void ScavengerGrounds_SacAbility_HasManaTapAndSacrificeADesertCosts()
    {
        var land = ScavengerGroundsFactory.Create(_alice);

        var ability = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        ability.Costs.OfType<ManaCostCost>().Single().Cost.Generic
            .Should().Be(2, "{2} additional mana cost");
        ability.Costs.Should().Contain(c => c is SacrificeFilteredCost,
            "the printed cost includes 'Sacrifice a Desert'");
    }

    [Fact]
    public void ScavengerGrounds_SacrificeADesertCost_PaysByMovingADesertToGraveyard()
    {
        var land = ScavengerGroundsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        var sacCost = ability.Costs.OfType<SacrificeFilteredCost>().Single();

        sacCost.CanPay(_alice).Should().BeTrue("Scavenger Grounds is itself a Desert (CR 701.16)");
        sacCost.Pay(_alice);

        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        land.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void ScavengerGrounds_Resolution_ExilesAllGraveyards()
    {
        var bob = new Player("Bob", 20);

        // Seed both graveyards.
        var aliceCard = new Creature("Dead Bear A", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        aliceCard.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(aliceCard);

        var bobCard = new Creature("Dead Bear B", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        bobCard.SetZone(ZoneType.Graveyard);
        bob.Zones.Graveyard.AddCard(bobCard);

        var land = ScavengerGroundsFactory.Create(_alice);

        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        // The "exile all graveyards" sweep reads ctx.Game.AllPlayers at
        // resolution — resolve with a live GameContext over both players.
        ResolveWithGame(ability, _alice, _alice, bob);

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty("Alice's graveyard is exiled");
        bob.Zones.Graveyard.GetCards().Should().BeEmpty("Bob's graveyard is exiled too (all graveyards)");
        _alice.Zones.Exile.GetCards().Should().Contain(aliceCard);
        bob.Zones.Exile.GetCards().Should().Contain(bobCard);
    }

    [Fact]
    public void ScavengerGrounds_Resolution_NoResolver_SweepsControllerGraveyardOnly()
    {
        var dead = new Creature("Dead Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        dead.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(dead);

        var land = ScavengerGroundsFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        ability.Resolve();

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Exile.GetCards().Should().Contain(dead);
    }

    private static void ResolveWithGame(
        ActivatedAbility ability, Player controller, params Player[] players)
    {
        var game = new Majik.Core.Game.GameContext(
            self: controller,
            allPlayers: players,
            activePlayer: controller,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));

        ability.ResolveAsync(agent: null, game: game).AsTask().GetAwaiter().GetResult();
    }
}
