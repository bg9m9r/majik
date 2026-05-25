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
/// Card: Exhume — Sorcery {1}{B} (Urza's Saga).
///   "Each player returns a creature card from their graveyard to the
///    battlefield."
///
/// Covers:
///   - Identity + <see cref="NamedCardFactory"/> dispatch.
///   - Single-player resolve (no allPlayersResolver) — caster reanimates
///     their own creature card.
///   - Multi-player resolve — each player reanimates their own creature
///     card under their own control (CR 110.2).
///   - Player with no creature card in graveyard is skipped without
///     affecting other players.
///   - ZoneService routing publishes <see cref="CardMovedEvent"/> per
///     reanimated creature (CR 603.6a).
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
    public void Exhume_Resolve_SinglePlayer_ReanimatesOwnCreature()
    {
        var alice = new Player("Alice", 20);

        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        giant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        foreach (var effect in ExhumeFactory.BuildResolveEffect(alice))
        {
            effect.Execute();
        }

        giant.Zone.Should().Be(ZoneType.Battlefield,
            "without a multi-player resolver, only the caster's graveyard is scanned");
        alice.Zones.Battlefield.GetCards().Should().Contain(giant);
        giant.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void Exhume_Resolve_EachPlayerReanimatesUnderOwnControl()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Each player has one creature card in their own graveyard.
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
        bobBear.Zone.Should().Be(ZoneType.Battlefield);

        alice.Zones.Battlefield.GetCards().Should().Contain(aliceGiant);
        alice.Zones.Battlefield.GetCards().Should().NotContain(bobBear,
            "Bob's creature goes to BOB's battlefield, not Alice's (CR 110.2)");

        bob.Zones.Battlefield.GetCards().Should().Contain(bobBear);
        bob.Zones.Battlefield.GetCards().Should().NotContain(aliceGiant);

        aliceGiant.Controller.Should().BeSameAs(alice,
            "each card returns under its owner's control");
        bobBear.Controller.Should().BeSameAs(bob);
    }

    [Fact]
    public void Exhume_Resolve_PlayerWithoutCreatureCard_IsSkipped()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Only Alice has a creature card in graveyard; Bob's graveyard is
        // empty (no skips, no exceptions).
        var aliceGiant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        aliceGiant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(aliceGiant);
        aliceGiant.SetZone(ZoneType.Graveyard);

        // Bob's graveyard has only a non-creature card — must not be
        // reanimated.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

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

        act.Should().NotThrow();

        aliceGiant.Zone.Should().Be(ZoneType.Battlefield);
        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "instants are not creature cards");
        bob.Zones.Battlefield.GetCards().Should().BeEmpty(
            "Bob had no creature card to reanimate");
    }

    [Fact]
    public void Exhume_Resolve_RoutesThroughZoneService_PublishesCardMovedEventPerPlayer()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var movedEvents = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(movedEvents.Add);

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
            zoneService: zones,
            allPlayersResolver: () => new[] { alice, bob }))
        {
            effect.Execute();
        }

        movedEvents.Should().HaveCount(2,
            "one CardMovedEvent per reanimated creature (CR 603.6a)");
        movedEvents.Should().Contain(e =>
            ReferenceEquals(e.Card, aliceGiant)
            && e.FromZone == ZoneType.Graveyard
            && e.ToZone == ZoneType.Battlefield);
        movedEvents.Should().Contain(e =>
            ReferenceEquals(e.Card, bobBear)
            && e.FromZone == ZoneType.Graveyard
            && e.ToZone == ZoneType.Battlefield);
    }
}
