using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="RavenousChupacabraFactory"/> — Creature — Beast
/// Horror {2}{B}{B} 2/2 with a single ETB trigger:
///   "When Ravenous Chupacabra enters, destroy target creature an opponent
///    controls."
///
/// Covers:
///   - Card identity (name, cost, type, subtypes, P/T, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - ETB trigger shape (1..1 target request "target creature an opponent
///     controls", scoped to battlefield active zone).
///   - Resolve: opponent's creature → destroyed.
///   - Resolve: own creature (illegal pick) → clean no-op.
///   - Resolve: target left battlefield → clean no-op.
///   - Resolve: no chosen target → clean no-op.
/// </summary>
[Trait("Color", "B")]
public class RavenousChupacabraFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility GetEtb(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>().Single();

    [Fact]
    public void RavenousChupacabra_IsBeastHorror_At2BB_TwoTwo()
    {
        var c = RavenousChupacabraFactory.Create(_alice);

        c.Name.Should().Be("Ravenous Chupacabra");
        c.ManaCost.Should().Be("{2}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void RavenousChupacabra_Etb_HasOpponentCreatureTargetRequest()
    {
        var c = RavenousChupacabraFactory.Create(_alice);
        var etb = GetEtb(c);

        etb.TargetRequests.Should().ContainSingle();
        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature").And.Contain("opponent");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void RavenousChupacabra_Etb_DestroysOpponentCreature()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var chup = RavenousChupacabraFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(chup);
        chup.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(chup);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        foreach (var e in etb.Effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void RavenousChupacabra_Etb_OwnCreatureTarget_NoOp()
    {
        // Chupacabra's controller is Alice; targeting Alice's own creature
        // violates "an opponent controls" — resolution guard no-ops.
        var ownBear = new Creature("Friendly Bears", "{1}{G}", 2, 2);
        ownBear.SetOwner(_alice);
        ownBear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ownBear);
        ownBear.SetZone(ZoneType.Battlefield);

        var chup = RavenousChupacabraFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(chup);
        chup.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(chup);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ownBear } });
        foreach (var e in etb.Effects) e.Execute();

        ownBear.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(ownBear);
    }

    [Fact]
    public void RavenousChupacabra_Etb_TargetLeftBattlefield_NoOp()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var chup = RavenousChupacabraFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(chup);
        chup.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(chup);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        // Bear bounces between trigger and resolution (CR 608.2b).
        _bob.Zones.Battlefield.RemoveCard(bear);
        _bob.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        foreach (var e in etb.Effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void RavenousChupacabra_Etb_NoChosenTarget_NoOp()
    {
        var chup = RavenousChupacabraFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(chup);
        chup.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(chup);

        Action act = () =>
        {
            foreach (var e in etb.Effects) e.Execute();
        };

        act.Should().NotThrow();
    }
}
