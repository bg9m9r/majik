using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.Factories;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// CR 702.21 — exercises the shared <see cref="Majik.Core.Keywords.WardTriggerWiring"/>
/// helper through the cards it newly wires: Tolarian Terror (Ward {3} — a
/// mana ward) and Sedgemoor Witch (Ward—Pay 3 life — a non-mana ward).
///
/// Asserts the four ward outcomes (CR 702.21e/f / 701.5b):
///   1. An opponent's spell targeting the warded creature TRIGGERS the ward.
///   2. A spell targeting something ELSE does not trigger.
///   3. The warded creature's controller's OWN spell does not trigger.
///   4. On resolution: pay-able + auto-pay → spell survives, cost charged;
///      can't pay → spell countered (→ owner's graveyard).
/// </summary>
public class WardTriggerWiringTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility WardTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>().Single(t =>
            t.Condition is EventTriggerCondition<TargetsChosenEvent>);

    /// <summary>
    /// Resolve the ward trigger's effects via the live async path against a
    /// <see cref="ResolutionContext"/> whose <c>Game.Stack</c> is
    /// <paramref name="stack"/> — exactly the path
    /// <see cref="Majik.Core.Services.StackResolver"/> uses in prod (the ward
    /// counters off <c>ctx.Game.Stack</c>, CR 608).
    /// </summary>
    private void ResolveWard(TriggeredAbility trigger, Majik.Core.Stack.Stack stack)
    {
        var gameCtx = new Majik.Core.Game.GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);
        var ctx = Majik.Core.Abilities.ResolutionContext.For(
            _alice, agent: null, game: gameCtx, chosenTargets: null);
        foreach (var e in trigger.Effects) e.ExecuteAsync(ctx).GetAwaiter().GetResult();
    }

    [Fact]
    public void TolarianTerror_OpponentSpellTargetsIt_TriggersWard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var terror = TolarianTerrorFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(terror);
        terror.SetZone(ZoneType.Battlefield);
        triggers.RegisterTriggeredAbility(WardTrigger(terror));

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(terror) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "an opponent's spell targeting Tolarian Terror triggers Ward {3} (CR 702.21e)");
    }

    [Fact]
    public void TolarianTerror_OpponentTargetsSomethingElse_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var terror = TolarianTerrorFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(terror);
        terror.SetZone(ZoneType.Battlefield);
        triggers.RegisterTriggeredAbility(WardTrigger(terror));

        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(bear) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "Ward fires only when the warded permanent itself is targeted (CR 702.21e)");
    }

    [Fact]
    public void TolarianTerror_ControllersOwnSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var terror = TolarianTerrorFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(terror);
        terror.SetZone(ZoneType.Battlefield);
        triggers.RegisterTriggeredAbility(WardTrigger(terror));

        // Alice targets her OWN Tolarian Terror — "an opponent controls" gate.
        var bolt = new Instant("Giant Growth", "G") { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(bolt, _alice, new[] { Target.Permanent(terror) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "Ward only triggers off a spell an OPPONENT controls (CR 702.21e)");
    }

    [Fact]
    public void TolarianTerror_ControllerCannotPay_SpellCountered()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var triggers = new TriggerManager(stack, bus);
        var terror = TolarianTerrorFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(terror);
        terror.SetZone(ZoneType.Battlefield);
        triggers.RegisterTriggeredAbility(WardTrigger(terror));

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(terror) });
        bolt.SetZone(ZoneType.Stack);
        stack.Push(spell);

        // Bob has no mana floating → can't pay Ward {3} → spell countered.
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));
        ResolveWard(WardTrigger(terror), stack);

        stack.GetAll().Should().NotContain(spell, "Bob can't pay {3}, so his spell is countered");
        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "a countered spell goes to its owner's graveyard (CR 701.5b)");
    }

    [Fact]
    public void TolarianTerror_ControllerPays_SpellSurvives()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var triggers = new TriggerManager(stack, bus);
        var terror = TolarianTerrorFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(terror);
        terror.SetZone(ZoneType.Battlefield);
        triggers.RegisterTriggeredAbility(WardTrigger(terror));

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(terror) });
        bolt.SetZone(ZoneType.Stack);
        stack.Push(spell);

        // Bob floats {3} → auto-pays the ward → spell survives.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(3));

        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));
        ResolveWard(WardTrigger(terror), stack);

        stack.GetAll().Should().Contain(spell, "Bob paid Ward {3}, so the spell stays on the stack");
        bolt.Zone.Should().Be(ZoneType.Stack);
        _bob.ManaPool.Total.Should().Be(0, "the {3} ward cost was charged");
    }

    [Fact]
    public void SedgemoorWitch_LifeWard_ControllerPaysLife_SpellSurvives()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var triggers = new TriggerManager(stack, bus);
        var witch = SedgemoorWitchFactory.Create(_alice, eventBus: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(witch);
        witch.SetZone(ZoneType.Battlefield);
        triggers.RegisterTriggeredAbility(WardTrigger(witch));

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(witch) });
        bolt.SetZone(ZoneType.Stack);
        stack.Push(spell);

        var lifeBefore = _bob.LifeTotal;
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));
        ResolveWard(WardTrigger(witch), stack);

        stack.GetAll().Should().Contain(spell, "Bob paid 3 life (Ward—Pay 3 life), so the spell survives");
        _bob.LifeTotal.Should().Be(lifeBefore - 3, "the pay-3-life ward cost was charged (CR 702.21c)");
    }
}
