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
/// Tests for Karn Liberated (New Phyrexia, {7}).
///
/// Covers:
///   - Card identity (Legendary Planeswalker, Karn subtype, loyalty 6,
///     mana cost {7}).
///   - Loyalty ability shape: three abilities at +4 / -3 / -14.
///   - Mechanic: +4 target opponent exiles a card from their hand
///     (auto-pick).
///   - Mechanic: -3 exile target opponent's creature (auto-pick).
///   - -14 ultimate: shape only — the loyalty ability exists at -14
///     cost and pays its loyalty even though the effect body is a
///     deferred no-op (restart-the-game is engine-foundational and not
///     shipped in this slice).
///   - NamedCardFactory dispatch.
/// </summary>
public class KarnLiberatedTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Karn_IsLegendaryPlaneswalker_Karn_6Loyalty_AtCost7()
    {
        var karn = KarnLiberatedFactory.Create(_alice);

        karn.Name.Should().Be("Karn Liberated");
        karn.ManaCost.Should().Be("{7}");
        karn.HasType(CardType.Planeswalker).Should().BeTrue();
        karn.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        karn.HasSubtype(CardSubtype.Karn).Should().BeTrue();
        karn.Loyalty.Should().Be(6);
        karn.StartingLoyalty.Should().Be(6);
        karn.Owner.Should().BeSameAs(_alice);
        karn.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Karn_HasThreeLoyaltyAbilities_Plus4_Minus3_Minus14()
    {
        var karn = KarnLiberatedFactory.Create(_alice);
        var loyaltyAbilities = karn.Abilities.OfType<LoyaltyAbility>().ToList();

        loyaltyAbilities.Should().HaveCount(3);
        loyaltyAbilities.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +4, -3, -14 });
    }

    [Fact]
    public void Karn_Plus4_TargetOpponentExilesACardFromHand()
    {
        // Bob has a card in hand; Karn's +4 forces him to exile it.
        var bobCard = new Card("Bob spell", "U") { Owner = _bob };
        _bob.Zones.Hand.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Hand);

        // Alice also has a card in hand — should not be touched (the
        // auto-pick targets the first opponent, not the controller).
        var aliceCard = new Card("Alice spell", "B") { Owner = _alice };
        _alice.Zones.Hand.AddCard(aliceCard);
        aliceCard.SetZone(ZoneType.Hand);

        var players = new[] { _alice, _bob };
        var karn = KarnLiberatedFactory.Create(_alice, () => players, targetResolver: null);
        _alice.Zones.Battlefield.AddCard(karn);
        karn.SetZone(ZoneType.Battlefield);

        var plus4 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +4);
        plus4.Activate();

        karn.Loyalty.Should().Be(10, "6 + 4 = 10");

        _bob.Zones.Hand.GetCards().Should().NotContain(bobCard);
        _bob.Zones.Exile.GetCards().Should().Contain(bobCard);
        bobCard.Zone.Should().Be(ZoneType.Exile);

        _alice.Zones.Hand.GetCards().Should().Contain(aliceCard,
            "+4 targets one opponent, not the controller");
    }

    [Fact]
    public void Karn_Minus3_ExileTargetPermanent()
    {
        // Bob controls a creature; Karn's -3 exiles it.
        var victim = new Creature("Goblin", "R", 1, 1);
        victim.SetOwner(_bob);
        victim.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);

        var karn = KarnLiberatedFactory.Create(
            _alice,
            allPlayersResolver: null,
            targetResolver: () => new[] { (Permanent)victim });
        _alice.Zones.Battlefield.AddCard(karn);
        karn.SetZone(ZoneType.Battlefield);

        var minus3 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -3);
        minus3.Activate();

        karn.Loyalty.Should().Be(3, "6 - 3 = 3");

        _bob.Zones.Battlefield.GetCards().Should().NotContain(victim);
        _bob.Zones.Exile.GetCards().Should().Contain(victim);
        victim.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Karn_Minus14_Ultimate_IsDeferredNoOp_StillPaysLoyaltyCost()
    {
        // Set loyalty up so -14 can legally activate (need ≥ 14).
        var karn = KarnLiberatedFactory.Create(_alice);
        karn.AddLoyalty(8); // 6 → 14

        var ultimate = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -14);
        ultimate.CanActivate().Should().BeTrue();
        ultimate.Activate();

        karn.Loyalty.Should().Be(0,
            "14 - 14 = 0; loyalty change applies even when the restart-the-game body is deferred (CR 606.3)");
    }

    [Fact]
    public void Karn_Plus4_NoResolverWired_LoyaltyStillTicksUp()
    {
        // Single-arg path passes no resolvers; the +4 effect no-ops but
        // the loyalty change still applies (CR 606.3).
        var karn = KarnLiberatedFactory.Create(_alice);

        // Give Bob a card in hand; the no-op resolver should leave it
        // alone.
        var bobCard = new Card("Bob spell", "U") { Owner = _bob };
        _bob.Zones.Hand.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Hand);

        var plus4 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +4);
        plus4.Activate();

        karn.Loyalty.Should().Be(10);
        _bob.Zones.Hand.GetCards().Should().Contain(bobCard,
            "no resolver wired → exile-from-hand effect is a silent no-op");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_KarnLiberated()
    {
        var card = NamedCardFactory.Create("Karn Liberated", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Karn Liberated");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Karn).Should().BeTrue();
        ((Planeswalker)card).Loyalty.Should().Be(6);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(3);
    }
}
