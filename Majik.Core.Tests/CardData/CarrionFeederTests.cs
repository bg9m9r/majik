using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="CarrionFeederFactory"/>.
///
/// Covers:
/// - Card identity (Creature — Zombie 1/1, mana cost {B}).
/// - Single activated ability with a sacrifice-another-creature cost.
/// - Activation places a +1/+1 counter on Carrion Feeder.
/// - Sacrifice cost cannot be paid without another creature.
/// - Can't-block restriction registered on the ContinuousEffectsService.
/// </summary>
public class CarrionFeederTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CarrionFeeder_IsZombieCreature()
    {
        var feeder = CarrionFeederFactory.Create(_alice);
        feeder.HasType(CardType.Creature).Should().BeTrue();
        feeder.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
    }

    [Fact]
    public void CarrionFeeder_NameAndCostAndPT()
    {
        var feeder = CarrionFeederFactory.Create(_alice);
        feeder.Name.Should().Be("Carrion Feeder");
        feeder.ManaCost.ToString().Should().Contain("B");
        feeder.Power.Should().Be(1);
        feeder.Toughness.Should().Be(1);
    }

    [Fact]
    public void CarrionFeeder_OwnerAndControllerAreSet()
    {
        var feeder = CarrionFeederFactory.Create(_alice);
        feeder.Owner.Should().BeSameAs(_alice);
        feeder.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CarrionFeeder_HasExactlyOneActivatedAbility()
    {
        var feeder = CarrionFeederFactory.Create(_alice);
        feeder.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void CarrionFeeder_Ability_HasSacrificeAnotherCreatureCost()
    {
        var feeder = CarrionFeederFactory.Create(_alice);
        var ability = feeder.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.OfType<SacrificeAnotherCreatureCost>().Should().HaveCount(1);
    }

    [Fact]
    public void Cost_CannotPay_WhenNoOtherCreatures()
    {
        var feeder = CarrionFeederFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(feeder);
        feeder.SetZone(ZoneType.Battlefield);

        var ability = feeder.Abilities.OfType<ActivatedAbility>().Single();
        var sac = ability.Costs.OfType<SacrificeAnotherCreatureCost>().Single();
        sac.CanPay(_alice).Should().BeFalse("the only creature is Carrion Feeder itself");
    }

    [Fact]
    public void Cost_CanPay_WhenAnotherCreatureExists()
    {
        var feeder = CarrionFeederFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(feeder);
        feeder.SetZone(ZoneType.Battlefield);

        var fodder = new Creature("Fodder", "1B", 1, 1);
        fodder.SetOwner(_alice); fodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(fodder);
        fodder.SetZone(ZoneType.Battlefield);

        var ability = feeder.Abilities.OfType<ActivatedAbility>().Single();
        var sac = ability.Costs.OfType<SacrificeAnotherCreatureCost>().Single();
        sac.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void Activation_PlacesPlusOnePlusOneCounter()
    {
        var feeder = CarrionFeederFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(feeder);
        feeder.SetZone(ZoneType.Battlefield);

        var fodder = new Creature("Fodder", "1B", 1, 1);
        fodder.SetOwner(_alice); fodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(fodder);
        fodder.SetZone(ZoneType.Battlefield);

        var ability = (CarrionFeederAbility)feeder.Abilities.OfType<ActivatedAbility>().Single();
        ability.SacrificeChoice.Target = fodder;

        // Pay cost + resolve effect.
        foreach (var c in ability.Costs) c.Pay(_alice);
        foreach (var e in ability.Effects) e.Execute();

        fodder.Zone.Should().Be(ZoneType.Graveyard, "sacrificed");
        feeder.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void CantBlock_RegisteredOnContinuousEffectsService()
    {
        var effects = new ContinuousEffectsService();
        var feeder = CarrionFeederFactory.Create(_alice, effects, replacements: null);
        _alice.Zones.Battlefield.AddCard(feeder);
        feeder.SetZone(ZoneType.Battlefield);

        // CR 509.1c — restriction installed by the factory makes Carrion
        // Feeder ineligible to block.
        effects.HasRestriction(feeder, CombatRestriction.CannotBlock)
            .Should().BeTrue();
    }

    [Fact]
    public void CantBlock_NotRegistered_WhenNoEffectsService()
    {
        // Single-arg dispatcher path: restriction not registered.
        var feeder = CarrionFeederFactory.Create(_alice);
        var effects = new ContinuousEffectsService();
        effects.HasRestriction(feeder, CombatRestriction.CannotBlock)
            .Should().BeFalse();
    }
}
