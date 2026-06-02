using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Vivien Reid (Core Set 2019, {3}{G}{G}).
///
/// Legendary Planeswalker — Vivien, starting loyalty 5. Oracle text
/// (Scryfall, verified):
///   "+1: Look at the top four cards of your library. You may reveal a
///        creature or land card from among them and put it into your hand.
///        Put the rest on the bottom of your library in a random order.
///    −3: Destroy target artifact, enchantment, or creature with flying.
///    −8: You get an emblem with 'Creatures you control get +2/+2 and have
///         vigilance, trample, and indestructible.'"
///
/// Covers:
///   - Card identity (Legendary Planeswalker — Vivien, loyalty 5, {3}{G}{G}),
///     materialised from the embedded JSON definition.
///   - Three loyalty abilities: +1, −3, −8.
///   - +1: digs four, puts a creature/land into hand, rest to bottom.
///   - −3: destroys a legal artifact / enchantment / flying creature.
///   - −8: mints the +2/+2-vigilance-trample-indestructible emblem.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "G")]
public class VivienReidFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Vivien_IsLegendaryPlaneswalker_Vivien_5Loyalty_AtCost3GG()
    {
        var vivien = VivienReidFactory.Create(_alice);

        vivien.Name.Should().Be("Vivien Reid");
        vivien.ManaCost.Should().Be("{3}{G}{G}");
        vivien.HasType(CardType.Planeswalker).Should().BeTrue();
        vivien.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        vivien.HasSubtype(CardSubtype.Vivien).Should().BeTrue();
        vivien.Loyalty.Should().Be(5);
        vivien.StartingLoyalty.Should().Be(5);
        vivien.Owner.Should().BeSameAs(_alice);
        vivien.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Vivien_HasThreeLoyaltyAbilities_Plus1_Minus3_Minus8()
    {
        var vivien = VivienReidFactory.Create(_alice);

        var loyalty = vivien.Abilities.OfType<LoyaltyAbility>().ToList();
        loyalty.Should().HaveCount(3);
        loyalty.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, -3, -8 });
    }

    // -----------------------------------------------------------------------
    // +1: Look at the top four cards of your library. You may reveal a
    //     creature or land card from among them and put it into your hand.
    //     Put the rest on the bottom of your library in a random order.
    // -----------------------------------------------------------------------

    [Fact]
    public void Plus1_PutsCreatureOrLandIntoHand_RestToBottom()
    {
        // Top four: a sorcery (ineligible), a creature (eligible — picked),
        // a land, and another sorcery. Order matters: GetCards() reads from
        // the front (top of library).
        var top1 = new Card("Lightning Bolt", "{R}",
            cardTypes: new[] { CardType.Sorcery }) { Owner = _alice };
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_alice);
        var land = new Card("Forest", "",
            cardTypes: new[] { CardType.Land }) { Owner = _alice };
        var top4 = new Card("Divination", "{2}{U}",
            cardTypes: new[] { CardType.Sorcery }) { Owner = _alice };
        var bottomMarker = new Card("Bottom Marker", "") { Owner = _alice };

        // Library order (front → back == top → bottom): top1..top4, then the
        // pre-existing bottom marker.
        foreach (var c in new ICard[] { top1, creature, land, top4, bottomMarker })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var vivien = VivienReidFactory.Create(_alice);
        vivien.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1).Activate();

        vivien.Loyalty.Should().Be(6); // 5 + 1

        // The first eligible card (the creature) goes to hand (no agent
        // registered ⇒ deterministic first-eligible pick).
        _alice.Zones.Hand.GetCards().Should().Contain(creature);

        // The other three revealed cards left the top and went to the
        // library's bottom; the picked creature is no longer in the library.
        _alice.Zones.Library.GetCards().Should().NotContain(creature);
        _alice.Zones.Library.GetCards().Should().Contain(top1);
        _alice.Zones.Library.GetCards().Should().Contain(land);
        _alice.Zones.Library.GetCards().Should().Contain(top4);
    }

    [Fact]
    public void Plus1_NoEligibleCard_DigsButHandUnchanged()
    {
        var s1 = new Card("Sorc1", "{R}", cardTypes: new[] { CardType.Sorcery }) { Owner = _alice };
        var s2 = new Card("Sorc2", "{R}", cardTypes: new[] { CardType.Sorcery }) { Owner = _alice };
        foreach (var c in new ICard[] { s1, s2 })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var vivien = VivienReidFactory.Create(_alice);
        vivien.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1).Activate();

        vivien.Loyalty.Should().Be(6);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // −3: Destroy target artifact, enchantment, or creature with flying.
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus3_DestroysTargetArtifact()
    {
        var artifact = new Artifact("Sol Ring", "{1}");
        artifact.SetOwner(_bob); _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield); artifact.SetController(_bob);

        var vivien = VivienReidFactory.Create(
            _alice, targetResolver: () => new[] { (Permanent)artifact }, triggers: null);

        vivien.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -3).Activate();

        vivien.Loyalty.Should().Be(2); // 5 - 3
        _bob.Zones.Battlefield.GetCards().Should().NotContain(artifact);
        _bob.Zones.Graveyard.GetCards().Should().Contain(artifact);
    }

    [Fact]
    public void Minus3_DestroysFlyingCreature_ButNotGroundCreature()
    {
        var flyer = new Creature("Air Elemental", "{3}{U}{U}", 4, 4);
        flyer.SetOwner(_bob); _bob.Zones.Battlefield.AddCard(flyer);
        flyer.SetZone(ZoneType.Battlefield); flyer.SetController(_bob);
        flyer.AddAbility(new KeywordAbility("Flying", flyer, _bob));

        var ground = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        ground.SetOwner(_bob); _bob.Zones.Battlefield.AddCard(ground);
        ground.SetZone(ZoneType.Battlefield); ground.SetController(_bob);

        // Ground creature first in the resolver — it is NOT a legal target,
        // so −3 must skip it and destroy the flyer.
        var vivien = VivienReidFactory.Create(
            _alice,
            targetResolver: () => new Permanent[] { ground, flyer },
            triggers: null);

        vivien.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -3).Activate();

        _bob.Zones.Graveyard.GetCards().Should().Contain(flyer);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(ground);
        _bob.Zones.Battlefield.GetCards().Should().Contain(ground);
    }

    // -----------------------------------------------------------------------
    // −8: You get an emblem with "Creatures you control get +2/+2 and have
    //     vigilance, trample, and indestructible."
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus8_RequiresEightLoyalty_AndMintsEmblem()
    {
        var vivien = VivienReidFactory.Create(_alice);

        var ult = vivien.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -8);
        ult.CanActivate().Should().BeFalse("5 loyalty is not enough for −8");

        vivien.AddLoyalty(3); // 5 + 3 = 8
        ult.CanActivate().Should().BeTrue();
        ult.Activate();

        vivien.Loyalty.Should().Be(0); // 8 - 8
        _alice.Emblems.Should().HaveCount(1);
    }
}
