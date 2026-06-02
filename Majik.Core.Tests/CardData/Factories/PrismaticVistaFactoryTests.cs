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
/// Tests for <c>PrismaticVistaFactory</c> (Modern Horizons).
///
/// Oracle: <c>{T}, Pay 1 life, Sacrifice Prismatic Vista: Search your library
/// for a basic land card, put it onto the battlefield, then shuffle.</c>
///
/// Mirrors the cycle-factory test shape used for the fetchland cycle in
/// <see cref="CycleFactoryTests"/>, narrowed to a single named card.
/// </summary>
[Trait("Color", "C")]
public class PrismaticVistaFactoryTests
{
    [Fact]
    public void HasSingleTapActivatedAbility()
    {
        var alice = new Player("Alice", 20);

        var land = NamedCardFactory.Create("Prismatic Vista", alice);

        var ability = land.Abilities.OfType<ActivatedAbility>()
            .Should().ContainSingle().Subject;
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(ac => ac.CostType == AdditionalCostType.Tap);
    }

    [Fact]
    public void Activation_FetchesBasicLand_PaysLife_AndSacrifices()
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

        var vista = NamedCardFactory.Create("Prismatic Vista", alice) as Land;
        vista.Should().NotBeNull();
        alice.Zones.Battlefield.AddCard(vista!);
        vista!.SetZone(ZoneType.Battlefield);

        var ability = vista!.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects)
        {
            eff.Execute();
        }

        // Basic forest fetched to battlefield; dual stays in library.
        alice.Zones.Battlefield.GetCards().Should().Contain(basicForest);
        alice.Zones.Library.GetCards().Should().Contain(stomping);
        alice.Zones.Library.GetCards().Should().NotContain(basicForest);

        // Vista self-sacrificed.
        alice.Zones.Graveyard.GetCards().Should().Contain(vista);
        alice.Zones.Battlefield.GetCards().Should().NotContain(vista);

        // Life paid (CR 119.4).
        alice.LifeTotal.Should().Be(19);
    }

    [Fact]
    public void Activation_NoBasicInLibrary_StillSacrificesAndPaysLife()
    {
        var alice = new Player("Alice", 20);

        // Library contains only a nonbasic land — search finds nothing,
        // but cost is still paid (CR 117.10 / 701.39c).
        var stomping = new Land(
            "Stomping Ground",
            supertypes: null,
            new[] { CardSubtype.Mountain, CardSubtype.Forest });
        alice.Zones.Library.AddCard(stomping);
        stomping.SetZone(ZoneType.Library);

        var vista = NamedCardFactory.Create("Prismatic Vista", alice) as Land;
        vista.Should().NotBeNull();
        alice.Zones.Battlefield.AddCard(vista!);
        vista!.SetZone(ZoneType.Battlefield);

        var ability = vista!.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects)
        {
            eff.Execute();
        }

        alice.Zones.Graveyard.GetCards().Should().Contain(vista);
        alice.LifeTotal.Should().Be(19);
        // Nonbasic untouched.
        alice.Zones.Library.GetCards().Should().Contain(stomping);
        alice.Zones.Battlefield.GetCards().Should().NotContain(stomping);
    }
}
