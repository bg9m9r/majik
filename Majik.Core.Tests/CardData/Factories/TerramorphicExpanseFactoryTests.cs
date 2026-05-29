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
/// Tests for <c>TerramorphicExpanseFactory</c> (Time Spiral / functional
/// reprint of Evolving Wilds).
///
/// Oracle (Scryfall, verified): <c>{T}, Sacrifice this land: Search your
/// library for a basic land card, put it onto the battlefield tapped, then
/// shuffle.</c>
///
/// Mirrors <see cref="PrismaticVistaFactoryTests"/> /
/// <c>WayfarersBaubleFactoryTests</c> — a sac-to-fetch land, but the fetched
/// basic enters <b>tapped</b> and there is no life payment / mana cost.
/// </summary>
public class TerramorphicExpanseFactoryTests
{
    [Fact]
    public void Dispatch_ReturnsLandWithPrintedName()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create("Terramorphic Expanse", alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Terramorphic Expanse");
    }

    [Fact]
    public void HasSingleTapActivatedAbility()
    {
        var alice = new Player("Alice", 20);

        var land = NamedCardFactory.Create("Terramorphic Expanse", alice);

        var ability = land.Abilities.OfType<ActivatedAbility>()
            .Should().ContainSingle().Subject;
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(ac => ac.CostType == AdditionalCostType.Tap);
    }

    [Fact]
    public void Activation_FetchesBasicLandTapped_AndSacrifices()
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

        var expanse = NamedCardFactory.Create("Terramorphic Expanse", alice) as Land;
        expanse.Should().NotBeNull();
        alice.Zones.Battlefield.AddCard(expanse!);
        expanse!.SetZone(ZoneType.Battlefield);

        var ability = expanse!.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects)
        {
            eff.Execute();
        }

        // Basic forest fetched to battlefield tapped; dual stays in library.
        alice.Zones.Battlefield.GetCards().Should().Contain(basicForest);
        basicForest.IsTapped.Should().BeTrue();
        alice.Zones.Library.GetCards().Should().Contain(stomping);
        alice.Zones.Library.GetCards().Should().NotContain(basicForest);

        // Expanse self-sacrificed.
        alice.Zones.Graveyard.GetCards().Should().Contain(expanse);
        alice.Zones.Battlefield.GetCards().Should().NotContain(expanse);

        // No life payment (unlike Prismatic Vista).
        alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Activation_NoBasicInLibrary_StillSacrifices()
    {
        var alice = new Player("Alice", 20);

        // Library contains only a nonbasic land — search finds nothing,
        // but the sacrifice cost is still paid (CR 701.39c).
        var stomping = new Land(
            "Stomping Ground",
            supertypes: null,
            new[] { CardSubtype.Mountain, CardSubtype.Forest });
        alice.Zones.Library.AddCard(stomping);
        stomping.SetZone(ZoneType.Library);

        var expanse = NamedCardFactory.Create("Terramorphic Expanse", alice) as Land;
        expanse.Should().NotBeNull();
        alice.Zones.Battlefield.AddCard(expanse!);
        expanse!.SetZone(ZoneType.Battlefield);

        var ability = expanse!.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects)
        {
            eff.Execute();
        }

        alice.Zones.Graveyard.GetCards().Should().Contain(expanse);
        alice.LifeTotal.Should().Be(20);
        // Nonbasic untouched.
        alice.Zones.Library.GetCards().Should().Contain(stomping);
        alice.Zones.Battlefield.GetCards().Should().NotContain(stomping);
    }
}
