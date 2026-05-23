using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for Wrenn and Realmbreaker (The Brothers' War, {3}{G}).
///
/// Covers:
///   - Card identity (Legendary Planeswalker, Wrenn subtype, loyalty 4,
///     mana cost {3}{G}).
///   - Loyalty ability shape: three abilities at +1 / -2 / -7.
///   - +1: mill 3 cards and return a land card from graveyard to hand.
///   - +1: mill 3 with no land available — no-op return, mill still
///     applies, loyalty still increments.
///   - -2: target nonland permanent (Bear) in graveyard → battlefield
///     under the activator's control; ETB triggers fire via ZoneService.
///   - -7: emblem minted into controller's Emblems collection.
///   - NamedCardFactory dispatch.
/// </summary>
public class WrennAndRealmbreakerTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void WrennAndRealmbreaker_IsLegendaryPlaneswalker_Wrenn_4Loyalty_AtCost3G()
    {
        var wrenn = WrennAndRealmbreakerFactory.Create(_alice);

        wrenn.Name.Should().Be("Wrenn and Realmbreaker");
        wrenn.ManaCost.Should().Be("{3}{G}");
        wrenn.HasType(CardType.Planeswalker).Should().BeTrue();
        wrenn.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        wrenn.HasSubtype(CardSubtype.Wrenn).Should().BeTrue();
        wrenn.Loyalty.Should().Be(4);
        wrenn.StartingLoyalty.Should().Be(4);
        wrenn.Owner.Should().BeSameAs(_alice);
        wrenn.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WrennAndRealmbreaker_HasThreeLoyaltyAbilities_Plus1_Minus2_Minus7()
    {
        var wrenn = WrennAndRealmbreakerFactory.Create(_alice);
        var loyaltyAbilities = wrenn.Abilities.OfType<LoyaltyAbility>().ToList();

        loyaltyAbilities.Should().HaveCount(3);
        loyaltyAbilities.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, -2, -7 });
    }

    [Fact]
    public void WrennAndRealmbreaker_Plus1_MillsThree_AndReturnsLandFromGraveyardToHand()
    {
        // Library: top three are an instant, an instant, and a Mountain.
        // After mill 3 all three are in graveyard; the +1 then returns
        // the Mountain to hand.
        var bolt1 = new Instant("Lightning Bolt", "R") { Owner = _alice };
        var bolt2 = new Instant("Lightning Bolt", "R") { Owner = _alice };
        var mountain = new Land("Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);

        _alice.Zones.Library.AddCard(bolt1);
        _alice.Zones.Library.AddCard(bolt2);
        _alice.Zones.Library.AddCard(mountain);

        var wrenn = WrennAndRealmbreakerFactory.Create(_alice);
        var plus1 = wrenn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        wrenn.Loyalty.Should().Be(5, "4 + 1 = 5");
        _alice.Zones.Library.GetCards().Should().BeEmpty(
            "mill 3 from a 3-card library mills all of them (CR 701.13)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { bolt1, bolt2 });
        _alice.Zones.Hand.GetCards().Should().Contain(mountain);
        mountain.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(mountain);
    }

    [Fact]
    public void WrennAndRealmbreaker_Plus1_MillsThree_NoLandAvailable_IsLegalNoOpReturn()
    {
        // Library: only nonland cards. The mill happens; the return is a
        // legal no-op ("you may").
        var bolt1 = new Instant("Lightning Bolt", "R") { Owner = _alice };
        var bolt2 = new Instant("Lightning Bolt", "R") { Owner = _alice };
        var bolt3 = new Instant("Lightning Bolt", "R") { Owner = _alice };

        _alice.Zones.Library.AddCard(bolt1);
        _alice.Zones.Library.AddCard(bolt2);
        _alice.Zones.Library.AddCard(bolt3);

        var wrenn = WrennAndRealmbreakerFactory.Create(_alice);
        var plus1 = wrenn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        wrenn.Loyalty.Should().Be(5, "loyalty +1 still applies");
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards()
            .Should().Contain(new ICard[] { bolt1, bolt2, bolt3 });
        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no land in graveyard — 'you may' return is a legal no-op");
    }

    [Fact]
    public void WrennAndRealmbreaker_Minus2_ReanimatesTargetNonlandPermanent_FromGraveyard()
    {
        // A Bear is a nonland permanent card. The -2 puts it onto the
        // battlefield under Alice's control.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var wrenn = WrennAndRealmbreakerFactory.Create(_alice);
        wrenn.AddLoyalty(0); // already at 4
        var minus2 = wrenn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -2);
        minus2.Activate();

        wrenn.Loyalty.Should().Be(2, "4 - 2 = 2");
        bear.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bear);
        bear.Controller.Should().BeSameAs(_alice,
            "the reanimated permanent enters under the activator's control (CR 110.2)");
    }

    [Fact]
    public void WrennAndRealmbreaker_Minus2_SkipsLandCardsInGraveyard()
    {
        // A land card in graveyard is NOT a valid -2 target (must be a
        // nonland permanent card). With nothing else in graveyard, the
        // -2 resolves with no reanimation.
        var mountain = new Land("Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(mountain);
        mountain.SetZone(ZoneType.Graveyard);

        var wrenn = WrennAndRealmbreakerFactory.Create(_alice);
        var minus2 = wrenn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -2);
        minus2.Activate();

        wrenn.Loyalty.Should().Be(2, "loyalty change still applies (CR 606.3)");
        mountain.Zone.Should().Be(ZoneType.Graveyard,
            "land cards aren't valid -2 targets — must stay in graveyard");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(mountain);
    }

    [Fact]
    public void WrennAndRealmbreaker_Minus2_RoutesThroughZoneService_PublishesCardMovedEvent()
    {
        // CR 603.6a — ETB triggers on the reanimated permanent must fire,
        // which requires the move to go through ZoneService.
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var zones = new ZoneService(eventBus: bus);

        var movedEvents = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var wrenn = WrennAndRealmbreakerFactory.Create(
            alice, zoneService: zones, allPlayersResolver: null);
        var minus2 = wrenn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -2);
        minus2.Activate();

        bear.Zone.Should().Be(ZoneType.Battlefield);
        movedEvents.Should().ContainSingle(
            e => ReferenceEquals(e.Card, bear)
                && e.FromZone == ZoneType.Graveyard
                && e.ToZone == ZoneType.Battlefield,
            "graveyard → battlefield routes through ZoneService so ETB triggers fire (CR 603.6a)");
    }

    [Fact]
    public void WrennAndRealmbreaker_Minus7_AddsEmblemToControllerCommandZone()
    {
        // Set loyalty high enough for -7 to be legal.
        var wrenn = WrennAndRealmbreakerFactory.Create(_alice);
        wrenn.AddLoyalty(3); // 4 → 7

        var ultimate = wrenn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -7);
        ultimate.CanActivate().Should().BeTrue();
        ultimate.Activate();

        wrenn.Loyalty.Should().Be(0, "7 - 7 = 0");
        _alice.Emblems.Should().HaveCount(1);
        _alice.Emblems[0].SourceName.Should().Contain("Wrenn and Realmbreaker");
        _alice.Emblems[0].Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WrennAndRealmbreaker()
    {
        var card = NamedCardFactory.Create("Wrenn and Realmbreaker", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Wrenn and Realmbreaker");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wrenn).Should().BeTrue();
        ((Planeswalker)card).Loyalty.Should().Be(4);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(3);
    }
}
