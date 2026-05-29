using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="TectonicEdgeFactory"/> — Worldwake nonbasic-land
/// destruction utility land with a {C} mana ability and a CR 602.5b
/// activation gate ("Activate only if an opponent controls four or more
/// lands"). Mirrors the Wasteland / Demolition Field destroy posture plus
/// the Magmatic Channeler / Sea Gate Wreckage activation-gate posture.
/// </summary>
public class TectonicEdgeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Land MakeLand(Player owner, string name)
    {
        var land = new Land(name);
        land.SetOwner(owner);
        land.SetController(owner);
        owner.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        return land;
    }

    [Fact]
    public void TectonicEdge_IsLand_NoSubtypes()
    {
        var land = TectonicEdgeFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Name.Should().Be("Tectonic Edge");
        land.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TectonicEdge()
    {
        var card = NamedCardFactory.Create("Tectonic Edge", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Tectonic Edge");
    }

    [Fact]
    public void TectonicEdge_HasManaAbility_AndSingleDestroyActivatedAbility()
    {
        var land = TectonicEdgeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        activated.TargetRequests.Should().HaveCount(1);
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);
        activated.TargetRequests[0].Description.Should().Contain("nonbasic land");
    }

    [Fact]
    public void ActivationGate_TrueWhenOpponentControlsFourOrMoreLands()
    {
        for (var i = 0; i < 4; i++)
        {
            MakeLand(_bob, $"Forest{i}");
        }

        TectonicEdgeFactory
            .OpponentControlsFourOrMoreLands(_alice, () => new[] { _alice, _bob })
            .Should().BeTrue();
    }

    [Fact]
    public void ActivationGate_FalseWhenNoOpponentHasFourLands()
    {
        // Bob only has three; Alice's own lands don't count toward the gate.
        for (var i = 0; i < 3; i++)
        {
            MakeLand(_bob, $"Forest{i}");
        }
        for (var i = 0; i < 6; i++)
        {
            MakeLand(_alice, $"Island{i}");
        }

        TectonicEdgeFactory
            .OpponentControlsFourOrMoreLands(_alice, () => new[] { _alice, _bob })
            .Should().BeFalse();
    }

    [Fact]
    public void TectonicEdge_Destroys_NonbasicLand_WhenGateOpen_AndSacrificesSelf()
    {
        // Bob controls four lands, one of them a nonbasic target.
        var target = MakeLand(_bob, "Karakas");
        for (var i = 0; i < 3; i++)
        {
            MakeLand(_bob, $"Mountain{i}");
        }

        var tectonicEdge = TectonicEdgeFactory.Create(_alice,
            allPlayersResolver: () => new[] { _alice, _bob });
        _alice.Zones.Battlefield.AddCard(tectonicEdge);
        tectonicEdge.SetZone(ZoneType.Battlefield);

        var activated = tectonicEdge.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        // {1} cost — top up Alice's mana pool.
        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(1));
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        activated.Resolve();

        target.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(target);
        tectonicEdge.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(tectonicEdge);
    }

    [Fact]
    public void TectonicEdge_GateClosed_NoOpDestroy_StillSacrifices()
    {
        // Bob has only three lands -> activation gate (CR 602.5b) is closed.
        // The cost was paid up front (CR 117.x), so Tectonic Edge is still
        // sacrificed but the destroy half does nothing.
        var target = MakeLand(_bob, "Karakas");
        for (var i = 0; i < 2; i++)
        {
            MakeLand(_bob, $"Mountain{i}");
        }

        var tectonicEdge = TectonicEdgeFactory.Create(_alice,
            allPlayersResolver: () => new[] { _alice, _bob });
        _alice.Zones.Battlefield.AddCard(tectonicEdge);
        tectonicEdge.SetZone(ZoneType.Battlefield);

        var activated = tectonicEdge.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(1));
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        activated.Resolve();

        target.Zone.Should().Be(ZoneType.Battlefield);
        tectonicEdge.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void TectonicEdge_TargetingBasicLand_IsNoOp_OnDestroy()
    {
        // Bob controls four lands; Alice targets a basic. CR 608.2b —
        // illegal target -> destroy does nothing; self still sacrificed.
        var basic = new Land(
            name: "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        basic.SetOwner(_bob);
        basic.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(basic);
        basic.SetZone(ZoneType.Battlefield);
        for (var i = 0; i < 3; i++)
        {
            MakeLand(_bob, $"Mountain{i}");
        }

        var tectonicEdge = TectonicEdgeFactory.Create(_alice,
            allPlayersResolver: () => new[] { _alice, _bob });
        _alice.Zones.Battlefield.AddCard(tectonicEdge);
        tectonicEdge.SetZone(ZoneType.Battlefield);

        var activated = tectonicEdge.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(1));
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { basic },
        });
        activated.Resolve();

        basic.Zone.Should().Be(ZoneType.Battlefield);
        tectonicEdge.Zone.Should().Be(ZoneType.Graveyard);
    }
}
