using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ExhumeFactory"/>.
///
/// Exhume — Sorcery {1}{B} (Urza's Saga):
///   "Each player returns a creature card from their graveyard to the
///    battlefield."
/// </summary>
public class ExhumeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Exhume_Identity()
    {
        var c = ExhumeFactory.Create(_alice);

        c.Name.Should().Be("Exhume");
        c.Should().BeOfType<Sorcery>();
        c.ManaCost.Should().Be("{1}{B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Exhume_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Exhume", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Exhume");
        c.ManaCost.Should().Be("{1}{B}");
    }

    [Fact]
    public void Exhume_Resolve_CasterOnly_ReturnsFirstCreatureCard()
    {
        var alice = new Player("Alice", 20);

        // Instant + Creature in graveyard — only the creature is eligible.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        giant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        foreach (var effect in ExhumeFactory.BuildResolveEffect(alice))
        {
            effect.Execute();
        }

        giant.Zone.Should().Be(ZoneType.Battlefield,
            "the creature is returned to the battlefield under its owner's control");
        alice.Zones.Battlefield.GetCards().Should().Contain(giant);
        giant.Controller.Should().BeSameAs(alice);

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "non-creature cards are not eligible");
    }

    [Fact]
    public void Exhume_Resolve_EachPlayerReturnsTheirOwnCreature()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var aliceGiant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        aliceGiant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(aliceGiant);
        aliceGiant.SetZone(ZoneType.Graveyard);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobBear.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(bobBear);
        bobBear.SetZone(ZoneType.Graveyard);

        foreach (var effect in ExhumeFactory.BuildResolveEffect(
            alice,
            zoneService: null,
            allPlayersResolver: () => new[] { alice, bob }))
        {
            effect.Execute();
        }

        aliceGiant.Zone.Should().Be(ZoneType.Battlefield);
        alice.Zones.Battlefield.GetCards().Should().Contain(aliceGiant);
        aliceGiant.Controller.Should().BeSameAs(alice,
            "each player returns to their OWN battlefield (printed wording)");

        bobBear.Zone.Should().Be(ZoneType.Battlefield);
        bob.Zones.Battlefield.GetCards().Should().Contain(bobBear);
        bobBear.Controller.Should().BeSameAs(bob,
            "Bob's creature returns under Bob's control");
    }

    [Fact]
    public void Exhume_Resolve_EmptyGraveyard_SkipsPlayer()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Only Bob has a creature in their graveyard.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var act = () =>
        {
            foreach (var effect in ExhumeFactory.BuildResolveEffect(
                alice,
                zoneService: null,
                allPlayersResolver: () => new[] { alice, bob }))
            {
                effect.Execute();
            }
        };

        act.Should().NotThrow("empty graveyard → that player is skipped");

        alice.Zones.Battlefield.GetCards().Should().BeEmpty(
            "Alice has no creature card to return");
        bob.Zones.Battlefield.GetCards().Should().Contain(bear,
            "Bob still returns his creature");
    }

    [Fact]
    public void Exhume_Resolve_RoutesThroughZoneService_PublishesCardMovedEvent()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var movedEvents = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        giant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        foreach (var effect in ExhumeFactory.BuildResolveEffect(alice, zoneService: zones))
        {
            effect.Execute();
        }

        movedEvents.Should().ContainSingle(
            e => ReferenceEquals(e.Card, giant)
                 && e.FromZone == ZoneType.Graveyard
                 && e.ToZone == ZoneType.Battlefield,
            "graveyard → battlefield routes through ZoneService so ETB triggers fire (CR 603.6a)");
    }
}
