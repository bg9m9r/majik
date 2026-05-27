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
        c.ManaCost.Should().Be("{1}{G}");
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
    public void ActivatedAbility_Has_ManaCost_And_RemoveCounterCost()
    {
        var feeder = SpikeFeederFactory.Create(_alice);

        var ability = feeder.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "{2} is the printed mana cost half of the activation");
        ability.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Should().ContainSingle(
            "the activation requires removing one +1/+1 counter from Spike Feeder");
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

        var ability = feeder.Abilities.OfType<ActivatedAbility>().Single();
        var counterCost = ability.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        var lifeBefore = alice.LifeTotal;
        var countersBefore = feeder.Counters.Count(CounterType.PlusOnePlusOne);

        // Pay only the counter cost (mana cost not exercised in this
        // unit test — handled by the cost framework + payment pipeline).
        counterCost.CanPay(alice).Should().BeTrue();
        counterCost.Pay(alice);

        foreach (var effect in ability.Effects) effect.Execute();

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

        var counterCost = feeder.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        counterCost.CanPay(_alice).Should().BeFalse(
            "no +1/+1 counters means the cost cannot be paid (CR 117.6)");
    }
}
