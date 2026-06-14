using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Stack;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ShrineOfBurningRageFactory"/> — Shrine of Burning
/// Rage (New Phyrexia, Artifact {2}).
///
/// Oracle text (Scryfall, 2026-06-14):
///   "At the beginning of your upkeep and whenever you cast a red spell, put
///    a charge counter on this artifact.
///    {3}, {T}, Sacrifice this artifact: It deals damage equal to the number
///    of charge counters on it to any target."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity ({2} Artifact).
/// - Upkeep trigger puts a charge counter (CR 500.4).
/// - Casting a RED spell puts a charge counter; a non-red spell does NOT
///   (CR 105.2a — color from mana-cost pips).
/// - {3},{T},Sacrifice burn: costs are {3} + tap + sacrifice; deals damage
///   equal to charge counters to a player target and sacrifices the Shrine
///   (CR 121.2 snapshot + CR 701.16).
/// </summary>
[Trait("Color", "C")]
public class ShrineOfBurningRageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewSpell(Player controller, string name, string manaCost)
    {
        var instant = new Instant(name, manaCost) { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ShrineOfBurningRage_Identity_ArtifactAtCost2()
    {
        var shrine = ShrineOfBurningRageFactory.Create(_alice);

        shrine.Name.Should().Be("Shrine of Burning Rage");
        shrine.Should().BeOfType<Artifact>();
        shrine.HasType(CardType.Artifact).Should().BeTrue();
        shrine.ManaCostValue.TotalValue.Should().Be(2, "Shrine of Burning Rage costs {2}");
        shrine.Owner.Should().BeSameAs(_alice);
        shrine.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Charge-counter accrual
    // -----------------------------------------------------------------------

    [Fact]
    public void UpkeepTrigger_PutsOneChargeCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var shrine = ShrineOfBurningRageFactory.Create(_alice, eventBus: bus, triggers: triggers);
        _alice.Zones.Battlefield.AddCard(shrine);
        shrine.SetZone(ZoneType.Battlefield);
        triggers.BindCard(shrine);

        bus.Publish(new StepStartedEvent(
            Majik.Core.StateMachine.StepStateType.Upkeep, _alice));
        ResolvePending(triggers, stack);

        shrine.Counters.Count(CounterType.Charge).Should().Be(1,
            "the upkeep trigger puts a charge counter (CR 500.4)");
    }

    [Fact]
    public void CastingRedSpell_PutsOneChargeCounter_NonRedSpellDoesNot()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var shrine = ShrineOfBurningRageFactory.Create(_alice, eventBus: bus, triggers: triggers);
        _alice.Zones.Battlefield.AddCard(shrine);
        shrine.SetZone(ZoneType.Battlefield);
        triggers.BindCard(shrine);

        // Red spell -> one counter.
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "Lightning Bolt", "{R}")));
        ResolvePending(triggers, stack);
        shrine.Counters.Count(CounterType.Charge).Should().Be(1,
            "casting a red spell puts a charge counter (CR 105.2a)");

        // Blue spell -> no additional counter.
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "Counterspell", "{U}{U}")));
        ResolvePending(triggers, stack);
        shrine.Counters.Count(CounterType.Charge).Should().Be(1,
            "a non-red spell does not trigger the charge counter");

        // Opponent's red spell -> no additional counter ("you cast").
        bus.Publish(new SpellCastEvent(NewSpell(_bob, "Shock", "{R}")));
        ResolvePending(triggers, stack);
        shrine.Counters.Count(CounterType.Charge).Should().Be(1,
            "only spells YOU cast trigger (CR 601 — 'you')");
    }

    private void ResolvePending(TriggerManager triggers, Majik.Core.Stack.Stack stack)
    {
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0)
        {
            stack.Pop()!.Resolve();
        }
    }

    // -----------------------------------------------------------------------
    // {3}, {T}, Sacrifice burn
    // -----------------------------------------------------------------------

    [Fact]
    public void BurnAbility_Costs_AreManaTapAndSacrifice()
    {
        var shrine = ShrineOfBurningRageFactory.Create(_alice);

        var burn = BurnAbility(shrine);

        burn.Costs.OfType<ManaCostCost>().Should().ContainSingle(c => c.Cost.TotalValue == 3,
            "the burn ability costs {3}");
        burn.Costs.OfType<AdditionalCost>().Count(c => c.CostType == AdditionalCostType.Tap)
            .Should().Be(1, "tap cost");
        burn.Costs.OfType<AdditionalCost>().Count(c => c.CostType == AdditionalCostType.Sacrifice)
            .Should().Be(1, "sacrifice cost");
        burn.TargetRequests.Should().ContainSingle(t => t.MinTargets == 1 && t.MaxTargets == 1,
            "deals damage to any target — exactly one target");
    }

    [Fact]
    public void BurnAbility_DealsDamageEqualToChargeCounters_AndSacrificesShrine()
    {
        var shrine = ShrineOfBurningRageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(shrine);
        shrine.SetZone(ZoneType.Battlefield);
        shrine.Counters.Add(CounterType.Charge, 3);

        var burn = BurnAbility(shrine);
        burn.SetChosenTargets(new[] { new object[] { _bob } });

        foreach (var e in burn.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(17, "Bob takes 3 damage (= charge counters) to any target (CR 119.3)");
        shrine.Zone.Should().Be(ZoneType.Graveyard,
            "the sacrifice cost moves the Shrine to its owner's graveyard (CR 701.16)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(shrine);
    }

    private static ActivatedAbility BurnAbility(Artifact shrine) =>
        shrine.Abilities.OfType<ActivatedAbility>().Single(a =>
            a.Costs.OfType<AdditionalCost>().Any(c => c.CostType == AdditionalCostType.Sacrifice));
}
