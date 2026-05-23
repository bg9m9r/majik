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
/// Tests for Wrenn and Six (Modern Horizons, {R}{G}).
///
/// Covers:
///   - Card identity (Legendary Planeswalker, Wrenn subtype, loyalty 3,
///     mana cost {R}{G}).
///   - Loyalty ability shape: three abilities at +1 / -1 / -7.
///   - Mechanic: +1 returns a land card from graveyard to hand (auto-pick).
///   - +1 with no land in graveyard is a legal no-op ("up to one").
///   - Mechanic: -7 emits an emblem into the controller's command zone.
///   - NamedCardFactory dispatch.
/// </summary>
public class WrennAndSixTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Wrenn_IsLegendaryPlaneswalker_Wrenn_3Loyalty_AtCostRG()
    {
        var wrenn = WrennAndSixFactory.Create(_alice);

        wrenn.Name.Should().Be("Wrenn and Six");
        wrenn.ManaCost.Should().Be("{R}{G}");
        wrenn.HasType(CardType.Planeswalker).Should().BeTrue();
        wrenn.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        wrenn.HasSubtype(CardSubtype.Wrenn).Should().BeTrue();
        wrenn.Loyalty.Should().Be(3);
        wrenn.StartingLoyalty.Should().Be(3);
        wrenn.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Wrenn_HasThreeLoyaltyAbilities_Plus1_Minus1_Minus7()
    {
        var wrenn = WrennAndSixFactory.Create(_alice);
        var loyaltyAbilities = wrenn.Abilities.OfType<LoyaltyAbility>().ToList();

        loyaltyAbilities.Should().HaveCount(3);
        loyaltyAbilities.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, -1, -7 });
    }

    [Fact]
    public void Wrenn_Plus1_ReturnsLandFromGraveyardToHand()
    {
        var mountain = new Land("Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(mountain);
        mountain.SetZone(ZoneType.Graveyard);

        var wrenn = WrennAndSixFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(wrenn);
        wrenn.SetZone(ZoneType.Battlefield);

        var plus1 = wrenn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        wrenn.Loyalty.Should().Be(4);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(mountain);
        _alice.Zones.Hand.GetCards().Should().Contain(mountain);
        mountain.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Wrenn_Plus1_NoLandInGraveyard_IsLegalNoOp()
    {
        // "Up to one" — empty selection is legal. The loyalty change still
        // applies but no card moves.
        var wrenn = WrennAndSixFactory.Create(_alice);

        var plus1 = wrenn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        wrenn.Loyalty.Should().Be(4, "loyalty +1 still applies");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Wrenn_Plus1_PrefersLand_LeavesNonLandsInGraveyard()
    {
        // Graveyard has both an instant and a land — the +1 should pick
        // the land (filtered) and leave the instant alone.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        _alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        var mountain = new Land("Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(mountain);
        mountain.SetZone(ZoneType.Graveyard);

        var wrenn = WrennAndSixFactory.Create(_alice);
        var plus1 = wrenn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        _alice.Zones.Hand.GetCards().Should().Contain(mountain);
        _alice.Zones.Hand.GetCards().Should().NotContain(bolt);
        _alice.Zones.Graveyard.GetCards().Should().Contain(bolt);
    }

    [Fact]
    public void Wrenn_Minus7_AddsEmblemToControllerCommandZone()
    {
        // Set loyalty up so -7 can legally activate.
        var wrenn = WrennAndSixFactory.Create(_alice);
        wrenn.AddLoyalty(5); // 3 → 8

        var ultimate = wrenn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -7);
        ultimate.CanActivate().Should().BeTrue();
        ultimate.Activate();

        wrenn.Loyalty.Should().Be(1, "8 - 7 = 1");
        _alice.Emblems.Should().HaveCount(1);
        _alice.Emblems[0].SourceName.Should().Contain("Wrenn and Six");
        _alice.Emblems[0].Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WrennAndSix()
    {
        var card = NamedCardFactory.Create("Wrenn and Six", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Wrenn and Six");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wrenn).Should().BeTrue();
        ((Planeswalker)card).Loyalty.Should().Be(3);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(3);
    }
}
