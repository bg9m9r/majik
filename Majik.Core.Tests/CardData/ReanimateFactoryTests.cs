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
/// Unit tests for <see cref="ReanimateFactory"/>.
///
/// Covers:
/// - Card identity (name, type, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Resolve effect: reanimates target creature card from caster's
///   graveyard to caster's battlefield and applies life loss = mana value.
/// - Resolve effect filter: ignores non-creature cards in graveyard.
/// - Resolve effect routes through ZoneService when supplied (CR 603.6a).
/// - Multi-graveyard scan via allPlayersResolver.
/// </summary>
public class ReanimateFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Reanimate_Identity()
    {
        var c = ReanimateFactory.Create(_alice);

        c.Name.Should().Be("Reanimate");
        c.Should().BeOfType<Sorcery>();
        c.ManaCost.Should().Be("{B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Reanimate_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Reanimate", _alice);

        c.Should().BeOfType<Sorcery>("Reanimate is a Sorcery");
        c.Name.Should().Be("Reanimate");
        c.ManaCost.Should().Be("{B}");
    }

    [Fact]
    public void Reanimate_Resolve_ReanimatesCreature_AndCasterLosesLifeEqualToMv()
    {
        var alice = new Player("Alice", 20);

        // Hill Giant — printed mv 4 ({3}{R}).
        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        giant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        foreach (var effect in ReanimateFactory.BuildResolveEffect(alice))
        {
            effect.Execute();
        }

        giant.Zone.Should().Be(ZoneType.Battlefield,
            "the target creature was reanimated to the caster's battlefield");
        alice.Zones.Graveyard.GetCards().Should().NotContain(giant);
        alice.Zones.Battlefield.GetCards().Should().Contain(giant);
        giant.Controller.Should().BeSameAs(alice,
            "the reanimated permanent enters under the caster's control (CR 110.2)");
        alice.LifeTotal.Should().Be(16, "caster loses life = reanimated creature's mana value (4)");
    }

    [Fact]
    public void Reanimate_Resolve_IgnoresNonCreatureCardsInGraveyard()
    {
        var alice = new Player("Alice", 20);

        // Instant in graveyard — not eligible.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        var act = () =>
        {
            foreach (var effect in ReanimateFactory.BuildResolveEffect(alice))
            {
                effect.Execute();
            }
        };

        act.Should().NotThrow(
            "no creature card in graveyard → resolve no-ops (CR 117.x no legal target)");
        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "instants are not creature cards — must remain in graveyard");
        alice.LifeTotal.Should().Be(20, "no reanimation → no life loss tail (CR 608.2c)");
    }

    [Fact]
    public void Reanimate_Resolve_RoutesThroughZoneService_PublishesCardMovedEvent()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var movedEvents = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        foreach (var effect in ReanimateFactory.BuildResolveEffect(alice, zoneService: zones))
        {
            effect.Execute();
        }

        bear.Zone.Should().Be(ZoneType.Battlefield);
        movedEvents.Should().ContainSingle(
            e => ReferenceEquals(e.Card, bear)
                 && e.FromZone == ZoneType.Graveyard
                 && e.ToZone == ZoneType.Battlefield,
            "graveyard → battlefield routes through ZoneService so ETB triggers fire (CR 603.6a)");
        alice.LifeTotal.Should().Be(18,
            "caster loses life = mana value (2) after the move");
    }

    [Fact]
    public void Reanimate_Resolve_ScansAllPlayersGraveyards_WhenResolverSupplied()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Bob's graveyard has the only creature card.
        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        giant.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        // Alice casts Reanimate; with the multi-player resolver she
        // reaches across to Bob's graveyard (CR 700.6 "a graveyard").
        foreach (var effect in ReanimateFactory.BuildResolveEffect(
            alice,
            zoneService: null,
            allPlayersResolver: () => new[] { alice, bob }))
        {
            effect.Execute();
        }

        giant.Zone.Should().Be(ZoneType.Battlefield);
        bob.Zones.Graveyard.GetCards().Should().NotContain(giant,
            "the creature card was removed from Bob's graveyard");
        alice.Zones.Battlefield.GetCards().Should().Contain(giant,
            "reanimated under the CASTER's control, not the original owner's (CR 110.2)");
        giant.Controller.Should().BeSameAs(alice);
        alice.LifeTotal.Should().Be(16, "caster loses life = mv (4) regardless of which graveyard the card came from");
    }
}
