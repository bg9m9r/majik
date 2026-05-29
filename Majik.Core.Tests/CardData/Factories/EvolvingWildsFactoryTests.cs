using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <c>EvolvingWildsFactory</c> (Conflux / many reprints).
///
/// Oracle: <c>{T}, Sacrifice Evolving Wilds: Search your library for a basic
/// land card, put it onto the battlefield tapped, then shuffle.</c>
///
/// Same shape as <see cref="WayfarersBaubleFactory"/> (tutor a basic land onto
/// the battlefield tapped) but as a Land with no mana payment — i.e.
/// Prismatic Vista minus the 1-life payment, plus the printed "tapped" rider.
/// </summary>
public class EvolvingWildsFactoryTests
{
    [Fact]
    public void Dispatch_ReturnsLandWithPrintedName()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create("Evolving Wilds", alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Evolving Wilds");
    }

    [Fact]
    public void HasSingleTapActivatedAbility()
    {
        var alice = new Player("Alice", 20);

        var land = NamedCardFactory.Create("Evolving Wilds", alice);

        var ability = land.Abilities.OfType<ActivatedAbility>()
            .Should().ContainSingle().Subject;
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(ac => ac.CostType == AdditionalCostType.Tap);
    }

    [Fact]
    public void Activation_FetchesBasicLandTapped_NoLifePaid_AndSacrifices()
    {
        var alice = new Player("Alice", 20);

        // Stage a basic + a nonbasic dual-typed land in library; activation
        // must pick the basic and leave the dual alone (CR 205.4a).
        var basicForest = new Land(
            "Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        var stomping = new Land(
            "Stomping Ground",
            supertypes: null,
            new[] { CardSubtype.Mountain, CardSubtype.Forest });
        alice.Zones.Library.AddCard(basicForest);
        alice.Zones.Library.AddCard(stomping);
        basicForest.SetZone(ZoneType.Library);
        stomping.SetZone(ZoneType.Library);

        var wilds = NamedCardFactory.Create("Evolving Wilds", alice) as Land;
        wilds.Should().NotBeNull();
        alice.Zones.Battlefield.AddCard(wilds!);
        wilds!.SetZone(ZoneType.Battlefield);

        var ability = wilds!.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects)
        {
            eff.Execute();
        }

        // Basic forest fetched to battlefield tapped; dual stays in library.
        alice.Zones.Battlefield.GetCards().Should().Contain(basicForest);
        basicForest.IsTapped.Should().BeTrue();
        alice.Zones.Library.GetCards().Should().Contain(stomping);
        alice.Zones.Library.GetCards().Should().NotContain(basicForest);

        // Wilds self-sacrificed.
        alice.Zones.Graveyard.GetCards().Should().Contain(wilds);
        alice.Zones.Battlefield.GetCards().Should().NotContain(wilds);

        // No life payment on Evolving Wilds (unlike Prismatic Vista).
        alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Activation_NoBasicInLibrary_StillSacrificesAndShuffles()
    {
        var alice = new Player("Alice", 20);

        // Library contains only a nonbasic land — search finds nothing,
        // but the cost (sacrifice) is still paid (CR 701.39c).
        var stomping = new Land(
            "Stomping Ground",
            supertypes: null,
            new[] { CardSubtype.Mountain, CardSubtype.Forest });
        alice.Zones.Library.AddCard(stomping);
        stomping.SetZone(ZoneType.Library);

        var wilds = NamedCardFactory.Create("Evolving Wilds", alice) as Land;
        wilds.Should().NotBeNull();
        alice.Zones.Battlefield.AddCard(wilds!);
        wilds!.SetZone(ZoneType.Battlefield);

        var ability = wilds!.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects)
        {
            eff.Execute();
        }

        alice.Zones.Graveyard.GetCards().Should().Contain(wilds);
        alice.LifeTotal.Should().Be(20);
        // Nonbasic untouched.
        alice.Zones.Library.GetCards().Should().Contain(stomping);
        alice.Zones.Battlefield.GetCards().Should().NotContain(stomping);
    }
}
