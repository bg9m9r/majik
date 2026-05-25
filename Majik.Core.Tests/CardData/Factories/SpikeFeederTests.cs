using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SpikeFeederFactory"/>.
///
/// Card: Spike Feeder — Creature — Spike {1}{G} 0/0 (Tempest).
///   "Spike Feeder enters with two +1/+1 counters on it.
///    {2}, Remove a +1/+1 counter from Spike Feeder: You gain 2 life."
/// </summary>
public class SpikeFeederTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOnBattlefield(Player owner, Permanent perm)
    {
        owner.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);
        perm.SetController(owner);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SpikeFeeder_Identity()
    {
        var c = SpikeFeederFactory.Create(_alice);

        c.Name.Should().Be("Spike Feeder");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spike).Should().BeTrue();
        c.BasePower.Should().Be(0);
        c.BaseToughness.Should().Be(0);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpikeFeeder_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Spike Feeder", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Spike Feeder");
        c.HasSubtype(CardSubtype.Spike).Should().BeTrue();
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Spike Feeder has one activated ability ({2}, remove counter: gain 2 life)");
    }

    // -----------------------------------------------------------------------
    // Enters-with-counters (CR 614.1d)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersWithCounters_RegistersReplacement_StampsEtbCounterIntent()
    {
        var bus = new ReplacementBus();
        var feeder = SpikeFeederFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: feeder, FromZone: ZoneType.Hand, ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var rewritten = bus.Apply(intent);

        rewritten.Should().NotBeNull();
        rewritten!.PlusOneCountersOnEnter.Should().Be(
            SpikeFeederFactory.EntersWithCountersAmount,
            "Spike Feeder enters with two +1/+1 counters (CR 614.1d)");
    }

    [Fact]
    public void EntersWithCounters_NoReplacementBus_NoRegistration()
    {
        // Shape-only path: no ReplacementBus → callers manually stamp the
        // counters via MarkEntersWithCounters. Replacement isn't registered
        // because there's no bus to register against.
        var bus = new ReplacementBus();
        SpikeFeederFactory.Create(_alice); // built without bus

        var foreign = SpikeFeederFactory.Create(_alice, replacements: bus);
        var intent = new ZoneMoveIntent(
            Card: foreign, FromZone: ZoneType.Hand, ToZone: ZoneType.Battlefield,
            Controller: _alice);
        var rewritten = bus.Apply(intent);
        rewritten!.PlusOneCountersOnEnter.Should().Be(2,
            "the bus-built feeder still wires through its own bus");
    }

    [Fact]
    public void MarkEntersWithCounters_StampsTwoPlusOnePlusOneCounters()
    {
        var feeder = SpikeFeederFactory.Create(_alice);

        SpikeFeederFactory.MarkEntersWithCounters(feeder);

        feeder.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Activated ability — {2}, Remove a +1/+1 counter: You gain 2 life.
    // -----------------------------------------------------------------------

    [Fact]
    public void Activated_CostShape_ManaPlusCounterRemoval()
    {
        var feeder = SpikeFeederFactory.Create(_alice);
        var activated = feeder.Abilities.OfType<ActivatedAbility>().Single();

        activated.Costs.Should().HaveCount(2);
        activated.Costs.Should().Contain(c => c is ManaCostCost);
        activated.Costs.Should().Contain(c => c is RemovePlusOnePlusOneCounterCost);

        var counterCost = activated.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();
        counterCost.Amount.Should().Be(1, "remove A +1/+1 counter — singular");
    }

    [Fact]
    public void Activated_NoCounters_CounterCostCannotPay()
    {
        var feeder = SpikeFeederFactory.Create(_alice);
        PutOnBattlefield(_alice, feeder);

        var counterCost = feeder.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        counterCost.CanPay(_alice).Should().BeFalse(
            "no +1/+1 counters → cost cannot be paid (CR 119.4)");
    }

    [Fact]
    public void Activated_WithCounters_CounterCostCanPay_AndPaysCorrectly()
    {
        var feeder = SpikeFeederFactory.Create(_alice);
        PutOnBattlefield(_alice, feeder);
        SpikeFeederFactory.MarkEntersWithCounters(feeder);

        var counterCost = feeder.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        counterCost.CanPay(_alice).Should().BeTrue();

        counterCost.Pay(_alice);

        feeder.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "one counter removed, one remains");
    }

    [Fact]
    public void Activated_Resolution_GainsTwoLife()
    {
        var feeder = SpikeFeederFactory.Create(_alice);
        PutOnBattlefield(_alice, feeder);
        SpikeFeederFactory.MarkEntersWithCounters(feeder);

        var startingLife = _alice.LifeTotal;
        var activated = feeder.Abilities.OfType<ActivatedAbility>().Single();

        foreach (var effect in activated.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(startingLife + SpikeFeederFactory.LifeGainedPerActivation,
            "resolution body grants the controller 2 life (CR 119.3)");
    }

    [Fact]
    public void Activated_TwoActivations_DrainCountersAndGainFourLife()
    {
        var feeder = SpikeFeederFactory.Create(_alice);
        PutOnBattlefield(_alice, feeder);
        SpikeFeederFactory.MarkEntersWithCounters(feeder);

        var activated = feeder.Abilities.OfType<ActivatedAbility>().Single();
        var counterCost = activated.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();
        var startingLife = _alice.LifeTotal;

        // Activation #1
        counterCost.CanPay(_alice).Should().BeTrue();
        counterCost.Pay(_alice);
        foreach (var effect in activated.Effects) effect.Execute();

        // Activation #2
        counterCost.CanPay(_alice).Should().BeTrue();
        counterCost.Pay(_alice);
        foreach (var effect in activated.Effects) effect.Execute();

        feeder.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "both counters spent");
        _alice.LifeTotal.Should().Be(startingLife + 4,
            "2 activations × 2 life each = 4 life total");

        // Activation #3 — out of counters, can't pay
        counterCost.CanPay(_alice).Should().BeFalse();
    }
}
