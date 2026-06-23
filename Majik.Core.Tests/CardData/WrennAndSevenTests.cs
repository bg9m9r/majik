using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Wrenn and Seven (Innistrad: Midnight Hunt, {4}{G}).
///
/// Legendary Planeswalker — Wrenn, starting loyalty 5. Oracle text
/// (verified against Scryfall):
///   "+1: Reveal the top four cards of your library. Put all land cards
///        revealed this way into your hand and the rest into your graveyard.
///    0: Put any number of land cards from your hand onto the battlefield
///        tapped.
///    −3: Create a green Treefolk creature token with reach and 'This
///        token's power and toughness are each equal to the number of lands
///        you control.'
///    −8: Return all permanent cards from your graveyard to your hand. You
///        get an emblem with 'You have no maximum hand size.'"
///
/// Covers ONLY this card's unique behaviour (the four loyalty abilities) plus
/// a single identity assert. Contract test (CardFactoryContractTests) already
/// asserts NamedCardFactory dispatch + well-formedness automatically.
/// </summary>
[Trait("Color", "G")]
public class WrennAndSevenTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Land BasicForest(Player owner)
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(owner);
        return forest;
    }

    [Fact]
    public void WrennAndSeven_IsLegendaryPlaneswalker_Wrenn_5Loyalty_AtCost4G()
    {
        var wrenn = WrennAndSevenFactory.Create(_alice);

        wrenn.Name.Should().Be("Wrenn and Seven");
        wrenn.ManaCost.Should().Be("{4}{G}");
        wrenn.HasType(CardType.Planeswalker).Should().BeTrue();
        wrenn.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        wrenn.HasSubtype(CardSubtype.Wrenn).Should().BeTrue();
        wrenn.Loyalty.Should().Be(5);
        wrenn.StartingLoyalty.Should().Be(5);
        wrenn.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WrennAndSeven_HasFourLoyaltyAbilities_Plus1_Zero_Minus3_Minus8()
    {
        var wrenn = WrennAndSevenFactory.Create(_alice);
        var loyaltyAbilities = wrenn.Abilities.OfType<LoyaltyAbility>().ToList();

        loyaltyAbilities.Should().HaveCount(4);
        loyaltyAbilities.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, 0, -3, -8 });
    }

    [Fact]
    public void WrennAndSeven_Plus1_PutsRevealedLandsInHand_RestInGraveyard()
    {
        // Top four of the library: Forest, Lightning Bolt, Forest, Lightning
        // Bolt. Library is FILO — add the bottom-most first so the top four
        // (last four added) are exactly those cards.
        var bolt1 = new Instant("Lightning Bolt", "R") { Owner = _alice };
        var forest1 = BasicForest(_alice);
        var bolt2 = new Instant("Lightning Bolt", "R") { Owner = _alice };
        var forest2 = BasicForest(_alice);

        foreach (var c in new ICard[] { bolt1, forest1, bolt2, forest2 })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var wrenn = WrennAndSevenFactory.Create(_alice);
        var plus1 = wrenn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        wrenn.Loyalty.Should().Be(6, "5 + 1 = 6");
        // CR 701.15 — both lands go to hand; the two nonland cards go to the
        // graveyard. The library is emptied of the revealed four.
        _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { forest1, forest2 });
        _alice.Zones.Hand.GetCards().Should().NotContain(new ICard[] { bolt1, bolt2 });
        _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { bolt1, bolt2 });
        _alice.Zones.Library.GetCards().Should()
            .NotContain(new ICard[] { forest1, forest2, bolt1, bolt2 });
        forest1.Zone.Should().Be(ZoneType.Hand);
        bolt1.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void WrennAndSeven_Plus1_FewerThanFourCards_RevealsWhatRemains()
    {
        // Library has only two cards (a land + a nonland). "Top four" reveals
        // as many as exist (CR 701 — reveal up to the count available).
        var forest = BasicForest(_alice);
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        foreach (var c in new ICard[] { bolt, forest })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var wrenn = WrennAndSevenFactory.Create(_alice);
        var plus1 = wrenn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        wrenn.Loyalty.Should().Be(6);
        _alice.Zones.Hand.GetCards().Should().Contain(forest);
        _alice.Zones.Graveyard.GetCards().Should().Contain(bolt);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void WrennAndSeven_Zero_PutsAllLandsFromHandOntoBattlefieldTapped()
    {
        // Hand: two Forests + a nonland. The 0 puts every land card from hand
        // onto the battlefield tapped ("any number" — v1 auto-picks all).
        var forest1 = BasicForest(_alice);
        var forest2 = BasicForest(_alice);
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        foreach (var c in new ICard[] { forest1, forest2, bolt })
        {
            _alice.Zones.Hand.AddCard(c);
            c.SetZone(ZoneType.Hand);
        }

        var wrenn = WrennAndSevenFactory.Create(_alice);
        var zero = wrenn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == 0);
        zero.Activate();

        wrenn.Loyalty.Should().Be(5, "0 loyalty change");
        _alice.Zones.Battlefield.GetCards().Should().Contain(new ICard[] { forest1, forest2 });
        forest1.Zone.Should().Be(ZoneType.Battlefield);
        forest2.Zone.Should().Be(ZoneType.Battlefield);
        ((Land)forest1).IsTapped.Should().BeTrue("lands enter tapped");
        ((Land)forest2).IsTapped.Should().BeTrue("lands enter tapped");
        _alice.Zones.Hand.GetCards().Should().Contain(bolt, "nonland stays in hand");
    }

    [Fact]
    public void WrennAndSeven_Minus3_CreatesGreenTreefolkTokenWithReach()
    {
        var wrenn = WrennAndSevenFactory.Create(_alice);
        var minus3 = wrenn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -3);
        minus3.Activate();

        wrenn.Loyalty.Should().Be(2, "5 - 3 = 2");
        var token = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Should().ContainSingle().Subject;
        token.IsToken.Should().BeTrue();
        token.HasSubtype(CardSubtype.Treefolk).Should().BeTrue();
        CardColors.GetColors(token).Should().BeEquivalentTo(new[] { ManaColor.Green });
        token.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Reach");
    }

    [Fact]
    public void WrennAndSeven_Minus3_TokenPowerToughness_EqualLandsYouControl()
    {
        // CR 604.3 / 613.2 Layer 7a — "This token's power and toughness are
        // each equal to the number of lands you control." Wired against a live
        // ContinuousEffectsService so the CDA registers + computes.
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);

        // Three lands on the battlefield under Alice's control.
        for (var i = 0; i < 3; i++)
        {
            var land = BasicForest(_alice);
            land.SetController(_alice);
            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }

        var wrenn = WrennAndSevenFactory.Create(
            _alice, effects, bus, landsYouControlSource: () => _alice.Zones.Battlefield.GetCards());
        var minus3 = wrenn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -3);
        minus3.Activate();

        var token = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Single(c => c.IsToken);

        var pt = effects.ComputePowerToughness(token);
        pt.Power.Should().Be(3, "3 lands controlled");
        pt.Toughness.Should().Be(3, "3 lands controlled");

        // Add a fourth land — CDA re-reads on Compute (CR 613.2). Publishing
        // the land's entry on the bus invalidates the effects-service memo
        // cache (any GameEvent bumps the generation), as it would in a real
        // game where the land enters through ZoneService.
        var extra = BasicForest(_alice);
        extra.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(extra);
        extra.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(extra, ZoneType.Library, ZoneType.Battlefield));

        var pt2 = effects.ComputePowerToughness(token);
        pt2.Power.Should().Be(4, "4 lands controlled now");
        pt2.Toughness.Should().Be(4);
    }

    [Fact]
    public void WrennAndSeven_Minus8_ReturnsPermanentCardsFromGraveyard_AndMintsEmblem()
    {
        // Graveyard: a creature card, a land card, and an instant card. The
        // -8 returns the permanent cards (creature + land) to hand; the
        // instant stays in the graveyard (CR 110.4a — instants aren't
        // permanent cards).
        var bear = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        var forest = BasicForest(_alice);
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        foreach (var c in new ICard[] { bear, forest, bolt })
        {
            _alice.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }

        var wrenn = WrennAndSevenFactory.Create(_alice);
        wrenn.AddLoyalty(3); // 5 → 8 so -8 is legal

        var ultimate = wrenn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -8);
        ultimate.CanActivate().Should().BeTrue();
        ultimate.Activate();

        wrenn.Loyalty.Should().Be(0, "8 - 8 = 0");
        _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { bear, forest });
        _alice.Zones.Graveyard.GetCards().Should().Contain(bolt,
            "instant cards aren't permanent cards (CR 110.4a)");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(new ICard[] { bear, forest });

        _alice.Emblems.Should().HaveCount(1);
        _alice.Emblems[0].SourceName.Should().Contain("Wrenn and Seven");
        _alice.Emblems[0].Controller.Should().BeSameAs(_alice);
    }
}
