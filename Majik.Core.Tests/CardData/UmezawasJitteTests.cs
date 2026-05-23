using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="UmezawasJitteFactory"/> (Betrayers of
/// Kamigawa, {2}).
///
/// Covers:
/// - Identity (name, type, Legendary, Equipment subtype, mana cost,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Combat-damage trigger: when equipped creature deals combat damage,
///   put two charge counters on Jitte (CR 510, CR 603.1).
/// - Combat-damage trigger fires for damage to creatures AND players.
/// - Three modal activated abilities — each costs Remove a charge
///   counter from Jitte (RemoveChargeCounterCost).
/// - Mode 1: 2 damage to any target (Player and Creature targets).
/// - Mode 2: target creature gets -1/-1 until end of turn (Layer 7c via
///   PumpUntilEndOfTurnEffect).
/// - Mode 3: you gain 2 life.
/// - Equip {2} activated ability.
/// </summary>
public class UmezawasJitteTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Jitte_Identity()
    {
        var c = UmezawasJitteFactory.Create(_alice);

        c.Name.Should().Be("Umezawa's Jitte");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Umezawa's Jitte is a Legendary Artifact");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Jitte_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Umezawa's Jitte", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Umezawa's Jitte");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "combat-damage trigger is attached");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(4,
            "three modal abilities + Equip {2}");
    }

    // -----------------------------------------------------------------------
    // Equip cost
    // -----------------------------------------------------------------------

    [Fact]
    public void Jitte_EquipAbility_HasGenericTwoCost()
    {
        var c = UmezawasJitteFactory.Create(_alice);

        // The Equip ability is the only activated ability whose cost set
        // includes a ManaCostCost (the three modal abilities pay a
        // RemoveChargeCounterCost only).
        var equip = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2,
            "Equip {2} is the printed activation cost");
    }

    [Fact]
    public void Jitte_ModalAbilities_EachCostRemoveAChargeCounter()
    {
        var c = UmezawasJitteFactory.Create(_alice);

        // All three modal abilities carry a RemoveChargeCounterCost (and
        // no mana cost). The Equip ability is the only one with a mana
        // cost.
        var modal = c.Abilities.OfType<ActivatedAbility>()
            .Where(a => a.Costs.OfType<RemoveChargeCounterCost>().Any())
            .ToList();

        modal.Should().HaveCount(3, "three printed modes → three activated abilities");
        modal.Should().AllSatisfy(a =>
            a.Costs.OfType<ManaCostCost>().Should().BeEmpty(
                "modal abilities are cost-paid by counter removal only"));
    }

    // -----------------------------------------------------------------------
    // Combat-damage trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void Jitte_CombatDamage_AddsTwoChargeCounters()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var jitte = UmezawasJitteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(jitte);
        jitte.SetZone(ZoneType.Battlefield);
        jitte.AttachTo(bear);

        jitte.Counters.Count(CounterType.Charge).Should().Be(0);

        // The equipped Bear deals 2 combat damage to Bob.
        var trigger = jitte.Abilities.OfType<TriggeredAbility>().Single();
        var dmgEvent = new CombatDamageDealtEvent(bear, _bob, 2);
        trigger.IsTriggered(dmgEvent).Should().BeTrue(
            "equipped creature dealing combat damage matches the trigger (CR 510)");

        foreach (var e in trigger.Effects) e.Execute();

        jitte.Counters.Count(CounterType.Charge).Should().Be(2,
            "the trigger puts two charge counters on Jitte");
    }

    [Fact]
    public void Jitte_CombatDamage_OnlyEquippedCreatureFires()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var other = new Creature("Other", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var jitte = UmezawasJitteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(jitte);
        jitte.SetZone(ZoneType.Battlefield);
        jitte.AttachTo(bear);

        var trigger = jitte.Abilities.OfType<TriggeredAbility>().Single();

        // A different creature dealing combat damage → no fire.
        var otherDmg = new CombatDamageDealtEvent(other, _bob, 2);
        trigger.IsTriggered(otherDmg).Should().BeFalse(
            "only the equipped creature's combat damage feeds the trigger");
    }

    [Fact]
    public void Jitte_CombatDamage_FiresForCreatureTargetToo()
    {
        // The oracle text deliberately omits "to a player" — Jitte
        // triggers on combat damage to ANY target (creature/planeswalker/
        // player).
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var blocker = new Creature("Blocker", "1G", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        var jitte = UmezawasJitteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(jitte);
        jitte.SetZone(ZoneType.Battlefield);
        jitte.AttachTo(bear);

        var trigger = jitte.Abilities.OfType<TriggeredAbility>().Single();
        // Bear deals 2 combat damage to Blocker (creature target).
        var dmg = new CombatDamageDealtEvent(bear, blocker, 2);

        trigger.IsTriggered(dmg).Should().BeTrue(
            "Jitte fires on combat damage to a creature, not just to a player");
    }

    // -----------------------------------------------------------------------
    // Mode 1: 2 damage to any target
    // -----------------------------------------------------------------------

    [Fact]
    public void Jitte_Mode1_Damage_DealsTwoToPlayer()
    {
        var jitte = UmezawasJitteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(jitte);
        jitte.SetZone(ZoneType.Battlefield);
        jitte.Counters.Add(CounterType.Charge, 3);

        var modal = jitte.Abilities.OfType<ActivatedAbility>()
            .Where(a => a.Costs.OfType<RemoveChargeCounterCost>().Any())
            .ToList();

        // Mode 1 is identified by its "any target" TargetRequest description.
        var dmgAbility = modal.Single(a =>
            a.TargetRequests.Count == 1 &&
            a.TargetRequests[0].Description == "any target");

        // Pay the cost: remove a charge counter.
        dmgAbility.Costs.Single().Pay(_alice);
        jitte.Counters.Count(CounterType.Charge).Should().Be(2,
            "activating consumes one charge counter");

        // Pick Bob as the target.
        dmgAbility.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        _bob.LifeTotal.Should().Be(20);
        foreach (var e in dmgAbility.Effects) e.Execute();
        _bob.LifeTotal.Should().Be(18, "Mode 1 deals 2 damage to the chosen target (CR 119.3)");
    }

    [Fact]
    public void Jitte_Mode1_Damage_DealsTwoToCreature()
    {
        var jitte = UmezawasJitteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(jitte);
        jitte.SetZone(ZoneType.Battlefield);
        jitte.Counters.Add(CounterType.Charge, 3);

        var blocker = new Creature("Blocker", "1G", 2, 3)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };

        var modal = jitte.Abilities.OfType<ActivatedAbility>()
            .Where(a => a.Costs.OfType<RemoveChargeCounterCost>().Any())
            .ToList();
        var dmgAbility = modal.Single(a =>
            a.TargetRequests.Count == 1 &&
            a.TargetRequests[0].Description == "any target");

        dmgAbility.Costs.Single().Pay(_alice);
        dmgAbility.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { blocker },
        });

        blocker.Damage.Should().Be(0);
        foreach (var e in dmgAbility.Effects) e.Execute();
        blocker.Damage.Should().Be(2,
            "Mode 1 deals 2 damage to the chosen creature");
    }

    // -----------------------------------------------------------------------
    // Mode 2: -1/-1 until end of turn
    // -----------------------------------------------------------------------

    [Fact]
    public void Jitte_Mode2_MinusOneMinusOne_AppliesUntilEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var blocker = new Creature("Blocker", "1G", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var jitte = UmezawasJitteFactory.Create(_alice, svc, triggers: null);
        _alice.Zones.Battlefield.AddCard(jitte);
        jitte.SetZone(ZoneType.Battlefield);
        jitte.Counters.Add(CounterType.Charge, 1);

        var modal = jitte.Abilities.OfType<ActivatedAbility>()
            .Where(a => a.Costs.OfType<RemoveChargeCounterCost>().Any())
            .ToList();
        var minusAbility = modal.Single(a =>
            a.TargetRequests.Count == 1 &&
            a.TargetRequests[0].Description == "target creature");

        minusAbility.Costs.Single().Pay(_alice);
        minusAbility.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { blocker },
        });

        blocker.GetPower().Should().Be(2);
        blocker.GetToughness().Should().Be(2);

        foreach (var e in minusAbility.Effects) e.Execute();

        blocker.GetPower().Should().Be(1, "-1 power EOT");
        blocker.GetToughness().Should().Be(1, "-1 toughness EOT");
    }

    // -----------------------------------------------------------------------
    // Mode 3: you gain 2 life
    // -----------------------------------------------------------------------

    [Fact]
    public void Jitte_Mode3_GainTwoLife()
    {
        var jitte = UmezawasJitteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(jitte);
        jitte.SetZone(ZoneType.Battlefield);
        jitte.Counters.Add(CounterType.Charge, 1);

        var modal = jitte.Abilities.OfType<ActivatedAbility>()
            .Where(a => a.Costs.OfType<RemoveChargeCounterCost>().Any())
            .ToList();
        // Mode 3 is the only modal ability with no target request.
        var lifeAbility = modal.Single(a => a.TargetRequests.Count == 0);

        lifeAbility.Costs.Single().Pay(_alice);
        jitte.Counters.Count(CounterType.Charge).Should().Be(0,
            "activating consumes the last charge counter");

        _alice.LifeTotal.Should().Be(20);
        foreach (var e in lifeAbility.Effects) e.Execute();
        _alice.LifeTotal.Should().Be(22, "Mode 3 gains the controller 2 life");
    }

    // -----------------------------------------------------------------------
    // Cost gating — cannot activate without a counter
    // -----------------------------------------------------------------------

    [Fact]
    public void Jitte_ModalCost_CannotPayWithoutAChargeCounter()
    {
        var jitte = UmezawasJitteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(jitte);
        jitte.SetZone(ZoneType.Battlefield);
        // No counters on Jitte.

        var modal = jitte.Abilities.OfType<ActivatedAbility>()
            .Where(a => a.Costs.OfType<RemoveChargeCounterCost>().Any())
            .ToList();

        modal.Should().AllSatisfy(a =>
        {
            var cost = a.Costs.OfType<RemoveChargeCounterCost>().Single();
            cost.CanPay(_alice).Should().BeFalse(
                "no charge counter on Jitte → no modal activation");
        });
    }
}
