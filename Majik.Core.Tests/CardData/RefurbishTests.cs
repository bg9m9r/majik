using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="RefurbishFactory"/> — Sorcery {3}{W} (Aether Revolt /
/// Dominaria).
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "Return target artifact card from your graveyard to the battlefield."
///
/// Refurbish is the clean "reanimate an artifact, no rider" sorcery — the
/// same graveyard → battlefield shape as <see cref="TrashForTreasureFactory"/>
/// but without the sacrifice additional cost or any life-loss tail.
///
/// Covers:
///   - Identity (Sorcery, {3}{W}, owner / controller) + NamedCardFactory dispatch.
///   - Resolve reanimates target artifact card from caster's graveyard (CR 701.20).
///   - Resolve filters out non-artifact graveyard cards.
///   - Resolve no-ops when the caster's graveyard has no artifact card (CR 117.x).
///   - Resolve routes through ZoneService so ETB triggers fire (CR 603.6a).
/// </summary>
public class RefurbishTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var card = RefurbishFactory.Create(_alice);

        card.Name.Should().Be("Refurbish");
        card.Should().BeOfType<Sorcery>();
        card.ManaCost.Should().Be("{3}{W}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Refurbish()
    {
        var card = NamedCardFactory.Create("Refurbish", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Refurbish");
        card.ManaCost.Should().Be("{3}{W}");
    }

    [Fact]
    public void Resolve_ReanimatesArtifactFromGraveyard()
    {
        var alice = new Player("Alice", 20);

        // Sol Ring — plain Artifact card, mv 1 ({1}).
        var solRing = new Artifact("Sol Ring", "{1}");
        solRing.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(solRing);
        solRing.SetZone(ZoneType.Graveyard);

        foreach (var fx in RefurbishFactory.BuildResolveEffect(alice))
        {
            fx.Execute();
        }

        solRing.Zone.Should().Be(ZoneType.Battlefield,
            "the targeted artifact card was reanimated to the caster's battlefield (CR 701.20)");
        alice.Zones.Graveyard.GetCards().Should().NotContain(solRing);
        alice.Zones.Battlefield.GetCards().Should().Contain(solRing);
        solRing.Controller.Should().BeSameAs(alice,
            "the reanimated permanent enters under the caster's control (CR 110.2)");
        alice.LifeTotal.Should().Be(20,
            "Refurbish has no printed life-loss rider — distinct from Reanimate");
    }

    [Fact]
    public void Resolve_IgnoresNonArtifactCardsInGraveyard()
    {
        var alice = new Player("Alice", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        foreach (var fx in RefurbishFactory.BuildResolveEffect(alice))
        {
            fx.Execute();
        }

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "Grizzly Bears is a non-artifact creature — predicate filters it out");
        alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Resolve_NoArtifactInGraveyard_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        foreach (var fx in RefurbishFactory.BuildResolveEffect(alice))
        {
            fx.Execute();
        }

        alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Resolve_PicksArtifactWhenMixedGraveyard()
    {
        var alice = new Player("Alice", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var bauble = new Artifact("Mishra's Bauble", "{0}");
        bauble.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bauble);
        bauble.SetZone(ZoneType.Graveyard);

        foreach (var fx in RefurbishFactory.BuildResolveEffect(alice))
        {
            fx.Execute();
        }

        bauble.Zone.Should().Be(ZoneType.Battlefield, "the artifact card was the reanimation target");
        bear.Zone.Should().Be(ZoneType.Graveyard, "the non-artifact creature stays in the graveyard");
    }

    [Fact]
    public void Resolve_RoutesThroughZoneService_PublishesCardMovedEvent()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var movedEvents = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var solRing = new Artifact("Sol Ring", "{1}");
        solRing.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(solRing);
        solRing.SetZone(ZoneType.Graveyard);

        foreach (var fx in RefurbishFactory.BuildResolveEffect(alice, zoneService: zones))
        {
            fx.Execute();
        }

        movedEvents.Should().ContainSingle(
            e => ReferenceEquals(e.Card, solRing)
                 && e.FromZone == ZoneType.Graveyard
                 && e.ToZone == ZoneType.Battlefield,
            "graveyard → battlefield routes through ZoneService so ETB triggers fire (CR 603.6a)");
    }
}
