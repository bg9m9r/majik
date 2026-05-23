using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Oko, Thief of Crowns (Throne of Eldraine, {1}{G}{U}).
///
/// Covers:
///   - Card identity (Legendary Planeswalker, Oko subtype, loyalty 4,
///     mana cost {1}{G}{U}).
///   - Loyalty ability shape: three abilities at +2 / +1 / -5.
///   - Mechanic: +2 spawns a Food token on Oko's controller's battlefield.
///   - Mechanic: +1 turns a target creature into a 3/3 Elk with no
///     abilities (Layer 4 type/subtype rewrite + Layer 6 ability strip +
///     Layer 7b BecomesPTEffect at 3/3).
///   - Mechanic: -5 exchanges control of a target opponent's
///     artifact-or-creature with one of Oko's controller's creatures —
///     each card flips controller and moves between battlefield zones.
///   - NamedCardFactory dispatch.
/// </summary>
public class OkoThiefOfCrownsTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Oko_IsLegendaryPlaneswalker_Oko_4Loyalty_AtCost1GU()
    {
        var oko = OkoThiefOfCrownsFactory.Create(_alice);

        oko.Name.Should().Be("Oko, Thief of Crowns");
        oko.ManaCost.Should().Be("{1}{G}{U}");
        oko.HasType(CardType.Planeswalker).Should().BeTrue();
        oko.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        oko.HasSubtype(CardSubtype.Oko).Should().BeTrue();
        oko.Loyalty.Should().Be(4);
        oko.StartingLoyalty.Should().Be(4);
        oko.Owner.Should().BeSameAs(_alice);
        oko.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Oko_HasThreeLoyaltyAbilities_Plus2_Plus1_Minus5()
    {
        var oko = OkoThiefOfCrownsFactory.Create(_alice);
        var loyaltyAbilities = oko.Abilities.OfType<LoyaltyAbility>().ToList();

        loyaltyAbilities.Should().HaveCount(3);
        loyaltyAbilities.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +2, +1, -5 });
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Oko()
    {
        var card = NamedCardFactory.Create("Oko, Thief of Crowns", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Oko, Thief of Crowns");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Oko).Should().BeTrue();
        ((Planeswalker)card).Loyalty.Should().Be(4);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(3);
    }

    // -----------------------------------------------------------------------
    // +2: Food token
    // -----------------------------------------------------------------------

    [Fact]
    public void Plus2_CreatesFoodToken_OnControllersBattlefield()
    {
        var oko = OkoThiefOfCrownsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(oko);
        oko.SetZone(ZoneType.Battlefield);

        var plus2 = oko.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +2);
        plus2.Activate();

        // Loyalty went 4 → 6.
        oko.Loyalty.Should().Be(6);

        // A Food artifact token now sits on Alice's battlefield.
        var foods = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.HasSubtype(CardSubtype.Food))
            .ToList();
        foods.Should().HaveCount(1, "the +2 spawns exactly one Food token");
        foods[0].Name.Should().Be("Food");
        foods[0].IsToken.Should().BeTrue();
        foods[0].Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // +1: Becomes 3/3 Elk Creature, loses abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void Plus1_TargetCreature_BecomesElk3_3_WithNoAbilities()
    {
        // Bob controls a Grizzly Bear (2/2 Bear) — Oko's +1 turns it into
        // a 3/3 Elk with no keyword abilities.
        var bear = new Creature("Grizzly Bear", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        bear.AddAbility(new KeywordAbility("Trample"));

        var effects = new ContinuousEffectsService();
        var oko = OkoThiefOfCrownsFactory.Create(
            _alice,
            effects: effects,
            battlefieldResolver: () => new Permanent[] { bear },
            allPlayersResolver: null);
        _alice.Zones.Battlefield.AddCard(oko);
        oko.SetZone(ZoneType.Battlefield);

        var plus1 = oko.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        oko.Loyalty.Should().Be(5, "4 + 1");

        // Compute(Grizzly Bear) — Layer 4 stamped Elk + Creature, Layer 7b
        // set base 3/3, Layer 6 stripped keywords.
        var chars = effects.Compute(bear);
        chars.Subtypes.Should().Contain(CardSubtype.Elk);
        chars.Subtypes.Should().NotContain(CardSubtype.Bear,
            "becomes-Elk overwrites the creature-subtype slot");
        chars.Types.Should().Contain(CardType.Creature);
        chars.Power.Should().Be(3);
        chars.Toughness.Should().Be(3);
        chars.Keywords.Should().BeEmpty("Layer 6 strip clears printed keywords");
    }

    [Fact]
    public void Plus1_NoResolver_NoEffectsRegistered_LoyaltyStillTicksUp()
    {
        // The single-arg path passes no battlefieldResolver; the +1 effect
        // no-ops but the loyalty change still applies (CR 606.3).
        var oko = OkoThiefOfCrownsFactory.Create(_alice);

        var plus1 = oko.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        oko.Loyalty.Should().Be(5, "loyalty change applies even when the body is a no-op");
    }

    // -----------------------------------------------------------------------
    // -5: Exchange control
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus5_ExchangeControl_SwapsBattlefieldAndController()
    {
        // Alice controls a 1/1 Goblin; Bob controls Sol Ring (artifact).
        var goblin = new Creature("Goblin Recruit", "{R}", 1, 1,
            subtypes: new[] { CardSubtype.Goblin });
        goblin.SetOwner(_alice);
        goblin.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);

        var solRing = new Artifact("Sol Ring", "{1}");
        solRing.SetOwner(_bob);
        solRing.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(solRing);
        solRing.SetZone(ZoneType.Battlefield);

        var oko = OkoThiefOfCrownsFactory.Create(
            _alice,
            effects: null,
            battlefieldResolver: null,
            allPlayersResolver: () => new[] { _alice, _bob });
        // Bump loyalty so -5 is legal (4 + 5 → 9, well above 5).
        oko.AddLoyalty(5);

        var minus5 = oko.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -5);
        minus5.CanActivate().Should().BeTrue();
        minus5.Activate();

        oko.Loyalty.Should().Be(4, "9 - 5");

        // Goblin now sits on Bob's battlefield + Sol Ring on Alice's.
        _alice.Zones.Battlefield.GetCards().Should().NotContain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(solRing);
        _alice.Zones.Battlefield.GetCards().Should().Contain(solRing);

        // Controllers are flipped; ownership is unchanged (CR 110.2).
        goblin.Controller.Should().BeSameAs(_bob);
        solRing.Controller.Should().BeSameAs(_alice);
        goblin.Owner.Should().BeSameAs(_alice);
        solRing.Owner.Should().BeSameAs(_bob);
    }

    [Fact]
    public void Minus5_NoOpponentPermanent_NoExchange_LoyaltyStillDecrements()
    {
        // Alice has a creature but Bob has nothing to exchange.
        var goblin = new Creature("Goblin Recruit", "{R}", 1, 1,
            subtypes: new[] { CardSubtype.Goblin });
        goblin.SetOwner(_alice);
        goblin.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);

        var oko = OkoThiefOfCrownsFactory.Create(
            _alice,
            effects: null,
            battlefieldResolver: null,
            allPlayersResolver: () => new[] { _alice, _bob });
        oko.AddLoyalty(5);

        var minus5 = oko.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -5);
        minus5.Activate();

        // Loyalty change applies (CR 606.3) even when the body bails out.
        oko.Loyalty.Should().Be(4);
        _alice.Zones.Battlefield.GetCards().Should().Contain(goblin,
            "no exchange target → goblin stays put");
        goblin.Controller.Should().BeSameAs(_alice);
    }
}
