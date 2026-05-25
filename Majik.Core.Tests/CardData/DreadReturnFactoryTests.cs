using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="DreadReturnFactory"/>.
///
/// Card: Dread Return — Sorcery {2}{B}{B} (Future Sight).
///   "Return target creature card from your graveyard to the battlefield.
///    Flashback—Sacrifice three creatures."
///
/// Covers:
///   - Identity + <see cref="NamedCardFactory"/> dispatch.
///   - Resolve reanimates a creature card from caster's OWN graveyard.
///   - No-op when caster's graveyard has no creature card (CR 117.x).
///   - ZoneService routing publishes <see cref="CardMovedEvent"/>.
///   - Flashback alt-cost: mana zero + cast only from graveyard;
///     OnResolved exiles the card (CR 702.34b).
///   - Flashback sacrifice rider: requires exactly three creatures
///     controlled by the caster; pays atomically.
/// </summary>
public class DreadReturnFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DreadReturn_Identity()
    {
        var c = DreadReturnFactory.Create(_alice);

        c.Name.Should().Be("Dread Return");
        c.Should().BeOfType<Sorcery>();
        c.ManaCost.Should().Be("{2}{B}{B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DreadReturn_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Dread Return", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Dread Return");
        c.ManaCost.Should().Be("{2}{B}{B}");
    }

    [Fact]
    public void DreadReturn_Resolve_ReanimatesCreatureFromCasterGraveyard()
    {
        var alice = new Player("Alice", 20);

        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        giant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        foreach (var effect in DreadReturnFactory.BuildResolveEffect(alice))
        {
            effect.Execute();
        }

        giant.Zone.Should().Be(ZoneType.Battlefield);
        alice.Zones.Battlefield.GetCards().Should().Contain(giant);
        alice.Zones.Graveyard.GetCards().Should().NotContain(giant);
        giant.Controller.Should().BeSameAs(alice,
            "the reanimated permanent enters under the caster's control (CR 110.2)");
    }

    [Fact]
    public void DreadReturn_Resolve_NoCreatureCardInGraveyard_NoOps()
    {
        var alice = new Player("Alice", 20);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        var act = () =>
        {
            foreach (var effect in DreadReturnFactory.BuildResolveEffect(alice))
            {
                effect.Execute();
            }
        };

        act.Should().NotThrow();
        bolt.Zone.Should().Be(ZoneType.Graveyard);
        alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void DreadReturn_Resolve_DoesNotReanimateFromOpponentGraveyard()
    {
        // "Target creature card from YOUR graveyard" — opponent grave
        // creatures must be ignored even when present.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobBear.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(bobBear);
        bobBear.SetZone(ZoneType.Graveyard);

        foreach (var effect in DreadReturnFactory.BuildResolveEffect(alice))
        {
            effect.Execute();
        }

        bobBear.Zone.Should().Be(ZoneType.Graveyard,
            "Bob's creature is in Bob's graveyard, not Alice's — Dread Return targets only the caster's graveyard");
        alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void DreadReturn_Resolve_RoutesThroughZoneService_PublishesCardMovedEvent()
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

        foreach (var effect in DreadReturnFactory.BuildResolveEffect(alice, zoneService: zones))
        {
            effect.Execute();
        }

        bear.Zone.Should().Be(ZoneType.Battlefield);
        movedEvents.Should().ContainSingle(
            e => ReferenceEquals(e.Card, bear)
                 && e.FromZone == ZoneType.Graveyard
                 && e.ToZone == ZoneType.Battlefield,
            "graveyard → battlefield routes through ZoneService so ETB triggers fire (CR 603.6a)");
    }

    // -----------------------------------------------------------------------
    // Flashback
    // -----------------------------------------------------------------------

    [Fact]
    public void FlashbackCost_IsManaZero_AndOnlyCastableFromGraveyard()
    {
        var dr = DreadReturnFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(dr);
        dr.SetZone(ZoneType.Graveyard);

        var fb = DreadReturnFactory.BuildFlashbackCost();
        fb.AlternativeManaCost.Should().Be(ManaCost.Zero);
        fb.Description.Should().Contain("Flashback");
        fb.CanCastFor(dr, _alice).Should().BeTrue();

        // Move to hand → no longer castable via flashback (CR 702.34).
        _alice.Zones.Graveyard.RemoveCard(dr);
        _alice.Zones.Hand.AddCard(dr);
        dr.SetZone(ZoneType.Hand);
        fb.CanCastFor(dr, _alice).Should().BeFalse();
    }

    [Fact]
    public void FlashbackOnResolved_ExilesTheCard_CR_702_34b()
    {
        var dr = DreadReturnFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(dr);
        dr.SetZone(ZoneType.Graveyard);

        var fb = DreadReturnFactory.BuildFlashbackCost();
        fb.OnResolved(dr, _alice);

        dr.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(dr);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(dr);
    }

    [Fact]
    public void FlashbackSacrificeRider_RequiresThreeCreatures()
    {
        var rider = DreadReturnFactory.BuildFlashbackAdditionalCosts()[0];
        rider.Should().BeOfType<SacrificeThreeCreaturesAdditionalCost>();

        // Zero creatures — cannot pay.
        rider.CanPay(_alice).Should().BeFalse();
        rider.Pay(_alice).Should().BeFalse();

        // Two creatures — still cannot pay.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        var bird = new Creature("Birds of Paradise", "{G}", 0, 1);
        SeedBattlefield(_alice, bear);
        SeedBattlefield(_alice, bird);
        rider.CanPay(_alice).Should().BeFalse();

        // Three creatures — pay atomically.
        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        SeedBattlefield(_alice, giant);
        rider.CanPay(_alice).Should().BeTrue();
        rider.Pay(_alice).Should().BeTrue();

        _alice.Zones.Battlefield.GetCards().Should().NotContain(new Creature[] { bear, bird, giant });
        _alice.Zones.Graveyard.GetCards().Should().Contain(new Creature[] { bear, bird, giant });
        bear.Zone.Should().Be(ZoneType.Graveyard);
        bird.Zone.Should().Be(ZoneType.Graveyard);
        giant.Zone.Should().Be(ZoneType.Graveyard);

        var sac = (SacrificeThreeCreaturesAdditionalCost)rider;
        sac.Sacrificed.Should().HaveCount(3);
        sac.Sacrificed.Should().Contain(new Creature[] { bear, bird, giant });
    }

    [Fact]
    public void FlashbackSacrificeRider_FiveCreaturesAvailable_OnlySacrificesThree()
    {
        var rider = DreadReturnFactory.BuildFlashbackAdditionalCosts()[0];

        var creatures = new List<Creature>();
        for (var i = 0; i < 5; i++)
        {
            var c = new Creature($"Saproling {i}", "{G}", 1, 1);
            SeedBattlefield(_alice, c);
            creatures.Add(c);
        }

        rider.CanPay(_alice).Should().BeTrue();
        rider.Pay(_alice).Should().BeTrue();

        _alice.Zones.Battlefield.GetCards().Should().HaveCount(2,
            "exactly three of five creatures are sacrificed");
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(3);
    }

    private static void SeedBattlefield(Player p, Creature c)
    {
        c.SetOwner(p);
        c.SetController(p);
        p.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
    }
}
