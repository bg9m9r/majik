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
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SigilOfDistinctionFactory"/>.
///
/// Covers:
/// - Identity (name, Artifact, Equipment subtype, mana cost {X}).
/// - NamedCardFactory dispatch.
/// - Enters-with-X charge counters via the caller-supplied X provider.
/// - Equip ability shape: sorcery-speed, RemoveChargeCounterCost, no mana,
///   target-creature-you-control candidate gatherer.
/// - Equip resolution attaches to the first controller creature.
/// - Dynamic +N/+N boost where N = charge counters on the Sigil itself, and
///   that removing a charge counter (paying the equip cost) shrinks it.
/// - Boost falls back to 0 when unequipped.
/// </summary>
public class SigilOfDistinctionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SigilOfDistinction_Identity()
    {
        var c = SigilOfDistinctionFactory.Create(_alice);

        c.Name.Should().Be("Sigil of Distinction");
        c.ManaCost.Should().Be("{X}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SigilOfDistinction_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sigil of Distinction", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Sigil of Distinction");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Enters with X charge counters
    // -----------------------------------------------------------------------

    [Fact]
    public void SigilOfDistinction_EtbEffect_AddsXChargeCounters()
    {
        var sigil = SigilOfDistinctionFactory.Create(
            _alice, continuousEffects: null, xValueProvider: () => 3, triggers: null);
        sigil.Zone = ZoneType.Battlefield;

        var etb = sigil.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var eff in etb.Effects) eff.Execute();

        sigil.Counters.Count(CounterType.Charge).Should().Be(3,
            "enters with X (=3) charge counters");
    }

    [Fact]
    public void SigilOfDistinction_ShapePath_EntersWithZeroCounters()
    {
        var sigil = SigilOfDistinctionFactory.Create(_alice);
        sigil.Zone = ZoneType.Battlefield;

        var etb = sigil.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var eff in etb.Effects) eff.Execute();

        sigil.Counters.Count(CounterType.Charge).Should().Be(0,
            "the shape-only path can't know X, so X=0");
    }

    // -----------------------------------------------------------------------
    // Equip — Remove a charge counter
    // -----------------------------------------------------------------------

    [Fact]
    public void SigilOfDistinction_EquipAbility_IsSorcerySpeed_AndCostIsRemoveChargeCounter()
    {
        var c = SigilOfDistinctionFactory.Create(_alice);

        var equip = c.Abilities.OfType<ActivatedAbility>().Single();

        equip.IsSorcerySpeed.Should().BeTrue(
            "Equip is a sorcery-speed activation per CR 702.6e");
        equip.Costs.Should().ContainSingle()
            .Which.Should().BeOfType<RemoveChargeCounterCost>(
                "the equip cost is 'Remove a charge counter from this Equipment', not mana");
        equip.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "Sigil's equip cost carries no mana pip");
    }

    [Fact]
    public void SigilOfDistinction_Equip_AttachesToFirstControllerCreature()
    {
        var sigil = SigilOfDistinctionFactory.Create(_alice);
        sigil.Zone = ZoneType.Battlefield;
        sigil.Counters.Add(CounterType.Charge, 2);

        var bear = new Creature("Bear", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);

        var equip = sigil.Abilities.OfType<ActivatedAbility>().Single();

        // No agent target supplied — falls back to first controller creature.
        foreach (var eff in equip.Effects) eff.Execute();

        sigil.AttachedTo.Should().BeSameAs(bear);
    }

    // -----------------------------------------------------------------------
    // Dynamic +N/+N boost (N = charge counters on the Sigil)
    // -----------------------------------------------------------------------

    [Fact]
    public void SigilOfDistinction_Equipped_BoostEqualsChargeCounters_AndShrinksWhenRemoved()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var sigil = SigilOfDistinctionFactory.Create(_alice, svc);
        sigil.Zone = ZoneType.Battlefield;
        sigil.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(sigil);
        sigil.Counters.Add(CounterType.Charge, 3);

        sigil.AttachTo(bear);

        bear.GetPower().Should().Be(2 + 3, "+3/+3 from three charge counters");
        bear.GetToughness().Should().Be(2 + 3);

        // Paying the equip cost removes a charge counter → boost shrinks.
        sigil.Counters.Remove(CounterType.Charge, 1);

        bear.GetPower().Should().Be(2 + 2, "+2/+2 after one counter removed");
        bear.GetToughness().Should().Be(2 + 2);
    }

    [Fact]
    public void SigilOfDistinction_Unattached_BoostIsZero()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var sigil = SigilOfDistinctionFactory.Create(_alice, svc);
        sigil.Zone = ZoneType.Battlefield;
        sigil.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(sigil);
        sigil.Counters.Add(CounterType.Charge, 5);
        // intentionally not attached

        bear.GetPower().Should().Be(2, "the boost gates on AttachedTo");
    }

    [Fact]
    public void SigilOfDistinction_CountChargeCounters_ReadsSigilOwnCounters()
    {
        var sigil = SigilOfDistinctionFactory.Create(_alice);
        sigil.Zone = ZoneType.Battlefield;

        SigilOfDistinctionFactory.CountChargeCounters(sigil).Should().Be(0);

        sigil.Counters.Add(CounterType.Charge, 4);
        SigilOfDistinctionFactory.CountChargeCounters(sigil).Should().Be(4);
    }
}
