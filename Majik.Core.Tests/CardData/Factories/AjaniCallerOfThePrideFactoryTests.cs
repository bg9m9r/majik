using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Ajani, Caller of the Pride (Magic 2013, {1}{W}{W}).
///
/// Legendary Planeswalker — Ajani, starting loyalty 4. Oracle text
/// (Scryfall, verified):
///   "+1: Put a +1/+1 counter on up to one target creature.
///    −3: Target creature gains flying and double strike until end of turn.
///    −8: Create X 2/2 white Cat creature tokens, where X is your life total."
///
/// Covers:
///   - Card identity (Legendary Planeswalker — Ajani, loyalty 4, {1}{W}{W}),
///     materialised from the embedded JSON definition.
///   - Three loyalty abilities: +1, −3, −8.
///   - +1: places a +1/+1 counter on up to one target creature (CR 122).
///   - −3: target creature gains flying + double strike until end of turn
///     (CR 613 layer 6, expires at cleanup CR 514.2).
///   - −8: creates X 2/2 white Cat tokens, X = controller's life total
///     (CR 111).
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "W")]
public class AjaniCallerOfThePrideFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Ajani_IsLegendaryPlaneswalker_Ajani_4Loyalty_AtCost1WW()
    {
        var ajani = AjaniCallerOfThePrideFactory.Create(_alice);

        ajani.Name.Should().Be("Ajani, Caller of the Pride");
        ajani.ManaCost.Should().Be("{1}{W}{W}");
        ajani.HasType(CardType.Planeswalker).Should().BeTrue();
        ajani.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        ajani.HasSubtype(CardSubtype.Ajani).Should().BeTrue();
        ajani.Loyalty.Should().Be(4);
        ajani.StartingLoyalty.Should().Be(4);
        ajani.Owner.Should().BeSameAs(_alice);
        ajani.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Ajani_HasThreeLoyaltyAbilities_Plus1_Minus3_Minus8()
    {
        var ajani = AjaniCallerOfThePrideFactory.Create(_alice);

        var loyalty = ajani.Abilities.OfType<LoyaltyAbility>().ToList();
        loyalty.Should().HaveCount(3);
        loyalty.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, -3, -8 });
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Ajani_AsLegendaryPlaneswalker()
    {
        var dispatched = NamedCardFactory.Create("Ajani, Caller of the Pride", _alice);

        dispatched.Should().BeOfType<Planeswalker>();
        dispatched.Name.Should().Be("Ajani, Caller of the Pride");
        dispatched.ManaCost.Should().Be("{1}{W}{W}");
        ((Planeswalker)dispatched).Loyalty.Should().Be(4);
    }

    // -----------------------------------------------------------------------
    // +1: Put a +1/+1 counter on up to one target creature.
    // -----------------------------------------------------------------------

    [Fact]
    public void Plus1_PlacesPlusOnePlusOneCounter_OnTargetCreature()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice); bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var ajani = AjaniCallerOfThePrideFactory.Create(
            _alice,
            plusOneTargetResolver: () => bear,
            minusThreeTargetResolver: null,
            zones: null);

        ajani.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1).Activate();

        ajani.Loyalty.Should().Be(5); // 4 + 1
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void Plus1_WithNoTarget_NoOps_ButLoyaltyStillApplies()
    {
        // "up to one target creature" — zero is a legal choice (CR 115.1b).
        var ajani = AjaniCallerOfThePrideFactory.Create(
            _alice,
            plusOneTargetResolver: () => null,
            minusThreeTargetResolver: null,
            zones: null);

        ajani.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1).Activate();

        ajani.Loyalty.Should().Be(5);
    }

    // -----------------------------------------------------------------------
    // −3: Target creature gains flying and double strike until end of turn.
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus3_GrantsFlyingAndDoubleStrike_UntilEndOfTurn()
    {
        var continuous = new ContinuousEffectsService();
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var ajani = AjaniCallerOfThePrideFactory.Create(
            _alice,
            plusOneTargetResolver: null,
            minusThreeTargetResolver: () => bear,
            zones: null);

        ajani.AddLoyalty(3); // 4 + 3 = 7, enough for −3 twice; just need ≥ 3
        ajani.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -3).Activate();

        bear.HasEffectiveKeyword("Flying").Should().BeTrue("CR 702.9 — granted flying");
        bear.HasEffectiveKeyword("Double strike").Should().BeTrue("CR 702.4 — granted double strike");

        // CR 514.2 — both grants expire at cleanup.
        continuous.ExpireEndOfTurn();
        bear.HasEffectiveKeyword("Flying").Should().BeFalse();
        bear.HasEffectiveKeyword("Double strike").Should().BeFalse();
    }

    [Fact]
    public void Minus3_WithNoTarget_NoOps()
    {
        var ajani = AjaniCallerOfThePrideFactory.Create(
            _alice,
            plusOneTargetResolver: null,
            minusThreeTargetResolver: () => null,
            zones: null);

        ajani.AddLoyalty(3); // 7
        var act = () => ajani.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -3).Activate();
        act.Should().NotThrow();
        ajani.Loyalty.Should().Be(4); // 7 - 3
    }

    // -----------------------------------------------------------------------
    // −8: Create X 2/2 white Cat creature tokens, X = your life total.
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus8_CreatesXWhiteCatTokens_WhereXIsLifeTotal()
    {
        // Drop life to a small number to keep the token count assertable.
        _alice.LifeTotal = 3;

        var ajani = AjaniCallerOfThePrideFactory.Create(_alice);
        ajani.AddLoyalty(4); // 4 + 4 = 8

        ajani.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -8).Activate();

        ajani.Loyalty.Should().Be(0); // 8 - 8

        var cats = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Cat))
            .ToList();

        cats.Should().HaveCount(3, "X = controller's life total (CR 111)");
        cats.Should().OnlyContain(c => c.Power == 2 && c.Toughness == 2);
        cats.Should().OnlyContain(c => c.GetEffectiveColors().Contains(Majik.Core.ValueObjects.ManaColor.White));
    }

    [Fact]
    public void Minus8_CannotActivate_BelowEightLoyalty()
    {
        var ajani = AjaniCallerOfThePrideFactory.Create(_alice);

        var ult = ajani.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -8);
        ult.CanActivate().Should().BeFalse("4 loyalty is not enough for −8");
    }
}
