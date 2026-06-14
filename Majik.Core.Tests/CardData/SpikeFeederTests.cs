using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Spike Feeder (Urza's Saga, {1}{G}, Creature — Spike 0/0).
///
/// Covers:
///   - Card identity (name, type, subtype, P/T, mana cost,
///     owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - <see cref="SpikeFeederFactory.MarkEntersWithCounters"/> stamps
///     the printed two +1/+1 counters (CR 614.1d / CR 122).
///   - Activated ability shape: <c>{2}</c> mana + a
///     <see cref="RemovePlusOnePlusOneCounterCost"/>.
///   - Activated ability resolution: controller gains 2 life
///     (CR 119.1) and a +1/+1 counter is consumed off the source.
///   - Activation cost gate: cannot pay the counter cost when the
///     source has no +1/+1 counters.
/// </summary>
public class SpikeFeederTests
{
    private readonly Player _alice = new("Alice", 20);

    // ------------------------------------------------------------------
    // Identity
    // ------------------------------------------------------------------

    [Fact]
    public void SpikeFeeder_Identity()
    {
        var c = SpikeFeederFactory.Create(_alice);

        c.Name.Should().Be("Spike Feeder");
        c.ManaCost.Should().Be("{1}{G}{G}");
        c.Power.Should().Be(0);
        c.Toughness.Should().Be(0);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spike).Should().BeTrue("Spike Feeder is a Spike");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SpikeFeeder()
    {
        var card = NamedCardFactory.Create("Spike Feeder", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Spike Feeder");
        card.HasSubtype(CardSubtype.Spike).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // ETB counters
    // ------------------------------------------------------------------

    [Fact]
    public void MarkEntersWithCounters_Stamps_TwoPlusOnePlusOneCounters()
    {
        var feeder = SpikeFeederFactory.Create(_alice);
        SpikeFeederFactory.MarkEntersWithCounters(feeder);

        feeder.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "Spike Feeder enters with exactly two +1/+1 counters (CR 614.1d)");
    }

    // ------------------------------------------------------------------
    // Activated ability shape
    // ------------------------------------------------------------------

    [Fact]
    public void ActivatedAbilities_HaveCorrectCostShapes()
    {
        var feeder = SpikeFeederFactory.Create(_alice);

        var activated = feeder.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(2,
            "Spike Feeder has two activated abilities: the {2} targeted pump and the free lifegain");

        // The targeted pump: "{2}, Remove a +1/+1 counter: Put a +1/+1 counter
        // on target creature." It is the one carrying a TargetRequest.
        var pump = activated.Single(a => a.TargetRequests.Count > 0);
        pump.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "{2} is the printed mana cost of the pump ability");
        pump.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Should().ContainSingle(
            "the pump activation removes one +1/+1 counter");

        // The lifegain: "Remove a +1/+1 counter: You gain 2 life." — NO mana
        // cost (free), which is what makes the Heliod combo loop.
        var gain = activated.Single(a => a.TargetRequests.Count == 0);
        gain.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "the lifegain ability has no mana cost in the printed text");
        gain.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Should().ContainSingle(
            "the lifegain activation removes one +1/+1 counter");
    }

    // ------------------------------------------------------------------
    // Activated ability resolution
    // ------------------------------------------------------------------

    [Fact]
    public void Activation_GainsTwoLife_AndConsumesCounter()
    {
        var alice = new Player("Alice", 20);
        var feeder = SpikeFeederFactory.Create(alice);
        SpikeFeederFactory.MarkEntersWithCounters(feeder);
        alice.Zones.Battlefield.AddCard(feeder);
        feeder.SetZone(ZoneType.Battlefield);

        // The free lifegain ability (no TargetRequest).
        var ability = feeder.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);
        var counterCost = ability.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        var lifeBefore = alice.LifeTotal;
        var countersBefore = feeder.Counters.Count(CounterType.PlusOnePlusOne);

        // Pay only the counter cost (mana cost not exercised in this
        // unit test — handled by the cost framework + payment pipeline).
        counterCost.CanPay(alice).Should().BeTrue();
        counterCost.Pay(alice);

        // Resolve through the ability so ResolutionContext.Controller is
        // populated (the re-sourceable gain-life effect reads its controller
        // off the context rather than a captured closure).
        ability.Resolve();

        alice.LifeTotal.Should().Be(lifeBefore + 2, "Spike Feeder grants 2 life on activation (CR 119.1)");
        feeder.Counters.Count(CounterType.PlusOnePlusOne)
            .Should().Be(countersBefore - 1,
                "one +1/+1 counter is removed as part of the activation cost");
    }

    [Fact]
    public void CounterCost_CannotPay_WhenNoCountersPresent()
    {
        var feeder = SpikeFeederFactory.Create(_alice);
        // No MarkEntersWithCounters — fresh card has zero counters.

        var counterCost = feeder.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0)
            .Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        counterCost.CanPay(_alice).Should().BeFalse(
            "no +1/+1 counters means the cost cannot be paid (CR 117.6)");
    }

    // ------------------------------------------------------------------
    // Pump ability — "{2}, Remove a +1/+1 counter: Put a +1/+1 counter on
    // target creature." This is the missing-effect that the Layer-B audit
    // surfaced (only the lifegain half was previously bound).
    // ------------------------------------------------------------------

    [Fact]
    public void PumpAbility_PutsCounterOnChosenTargetCreature()
    {
        var alice = new Player("Alice", 20);
        var feeder = SpikeFeederFactory.Create(alice);
        SpikeFeederFactory.MarkEntersWithCounters(feeder);
        alice.Zones.Battlefield.AddCard(feeder);
        feeder.SetZone(ZoneType.Battlefield);

        // A separate creature to receive the counter.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        bear.SetController(alice);
        alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var pump = feeder.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count > 0);

        // Pay the remove-counter cost half (mana handled by the framework).
        var counterCost = pump.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();
        counterCost.CanPay(alice).Should().BeTrue();
        counterCost.Pay(alice);

        // Choose the bear as the target, then resolve through the ability so
        // the re-sourceable pump effect reads ChosenTargets off the context.
        pump.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        pump.Resolve();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the pump puts one +1/+1 counter on the chosen target creature");
        feeder.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "one of Spike Feeder's two counters was removed to pay the cost");
    }
}
