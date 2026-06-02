using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SeaGateStormcallerFactory"/>.
///
/// Sea Gate Stormcaller (Zendikar Rising, {1}{U}). Creature — Human Wizard
/// 2/1. Oracle:
///   "Kicker {4}{U}
///    When this creature enters, copy the next instant or sorcery spell
///    with mana value 2 or less you cast this turn when you cast it. If
///    this creature was kicked, copy that spell twice instead. You may
///    choose new targets for the copies."
///
/// Coverage:
/// - Identity (name, type, subtypes, cost, P/T, owner/controller).
/// - NamedCardFactory dispatch.
/// - Structural ETB trigger (CR 603.6a) over CardMovedEvent → Battlefield.
/// - ETB resolution registers a delayed copy trigger; unkicked → 1 copy.
/// - ETB resolution when kicked → 2 copies (CR 707.10).
/// - The delayed trigger ignores instants/sorceries with MV > 2.
/// - The delayed trigger ignores creature spells.
/// - The delayed trigger ignores opponent casts (controller-gated).
/// - Kicker {4}{U} additional cost is built (CR 702.33).
/// - Shape-only path (no TriggerManager) doesn't throw.
/// </summary>
[Trait("Color", "U")]
public class SeaGateStormcallerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    /// <summary>
    /// Build an observable instant spell. <paramref name="counter"/> is a
    /// single-element box incremented once per re-execution of the spell's
    /// effect list — SpellCopier re-runs the effects, so this counts copies.
    /// </summary>
    private static Majik.Core.Spells.Spell ObservableInstant(
        Player controller, out int[] counter, string cost = "R", string name = "Bolt")
    {
        var box = new int[1];
        counter = box;
        var instant = new Instant(name, cost) { Owner = controller };
        return new Majik.Core.Spells.Spell(
            instant, controller,
            effects: new IEffect[] { new Effect("count", () => box[0]++) });
    }

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void SeaGateStormcaller_Identity()
    {
        var c = SeaGateStormcallerFactory.Create(_alice);

        c.Name.Should().Be("Sea Gate Stormcaller");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{U}");
        c.ManaCostValue.TotalValue.Should().Be(2);
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // ── Structural ETB trigger ──────────────────────────────────────────

    [Fact]
    public void Card_HasStructuralEtbTrigger()
    {
        var card = SeaGateStormcallerFactory.Create(_alice);

        var triggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "Sea Gate Stormcaller prints one triggered ability — the ETB copy rider.");

        var etb = triggers[0];
        etb.Source.Should().BeSameAs(card);
        etb.Condition.Should().BeOfType<EventTriggerCondition<CardMovedEvent>>();
    }

    // ── ETB → delayed copy, unkicked (1 copy) ───────────────────────────

    [Fact]
    public void Etb_Unkicked_RegistersDelayedTrigger_CopiesNextInstantOnce()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = SeaGateStormcallerFactory.Create(_alice, triggers, stack);

        // Fire the ETB trigger directly (unkicked → 1 copy).
        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        var spell = ObservableInstant(_alice, out var count, cost: "R");
        count[0].Should().Be(0, "no copy yet — only the cast will fire the delayed trigger");

        bus.Publish(new SpellCastEvent(spell));
        triggers.PendingCount.Should().Be(1, "delayed trigger fired on the qualifying instant cast");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        count[0].Should().Be(1, "unkicked Stormcaller copies the spell once (CR 707.10)");
    }

    // ── ETB → delayed copy, kicked (2 copies) ───────────────────────────

    [Fact]
    public void Etb_Kicked_CopiesNextInstantTwice()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = SeaGateStormcallerFactory.Create(_alice, triggers, stack);
        card.SetWasKicked(true);

        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        var spell = ObservableInstant(_alice, out var count, cost: "R");

        bus.Publish(new SpellCastEvent(spell));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        count[0].Should().Be(2, "kicked Stormcaller copies the spell twice (CR 707.10)");
    }

    // ── Direct delayed-copy helper exercises copy count ─────────────────

    [Fact]
    public void RegisterDelayedCopy_CopiesSorcery_HonoursCount()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        SeaGateStormcallerFactory.RegisterDelayedCopy(triggers, stack, _alice, copyCount: 2);

        var sorcery = new Sorcery("Lava", "1R") { Owner = _alice };
        var box = new int[1];
        var spell = new Majik.Core.Spells.Spell(
            sorcery, _alice, effects: new IEffect[] { new Effect("c", () => box[0]++) });

        bus.Publish(new SpellCastEvent(spell));
        triggers.PendingCount.Should().Be(1, "MV-2 sorcery qualifies");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        box[0].Should().Be(2);
    }

    // ── MV gate ─────────────────────────────────────────────────────────

    [Fact]
    public void DelayedTrigger_IgnoresInstantsWithManaValueAbove2()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        SeaGateStormcallerFactory.RegisterDelayedCopy(triggers, stack, _alice, copyCount: 1);

        // MV 3 instant — does not qualify ("mana value 2 or less").
        var big = new Instant("Big Spell", "2R") { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(big, _alice);

        bus.Publish(new SpellCastEvent(spell));
        triggers.PendingCount.Should().Be(0,
            "instant with mana value 3 exceeds the MV≤2 gate — delayed trigger must not fire");
    }

    // ── Type gate ───────────────────────────────────────────────────────

    [Fact]
    public void DelayedTrigger_IgnoresCreatureSpells()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        SeaGateStormcallerFactory.RegisterDelayedCopy(triggers, stack, _alice, copyCount: 1);

        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        bus.Publish(new SpellCastEvent(new Majik.Core.Spells.Spell(bear, _alice)));
        triggers.PendingCount.Should().Be(0,
            "a creature spell isn't instant/sorcery — delayed trigger must not fire");
    }

    // ── Controller gate ─────────────────────────────────────────────────

    [Fact]
    public void DelayedTrigger_IgnoresOpponentsSpells()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        SeaGateStormcallerFactory.RegisterDelayedCopy(triggers, stack, _alice, copyCount: 1);

        var bobBolt = new Instant("Bob's Bolt", "R") { Owner = _bob };
        bus.Publish(new SpellCastEvent(new Majik.Core.Spells.Spell(bobBolt, _bob)));
        triggers.PendingCount.Should().Be(0,
            "the delayed trigger gates on the controller — opponent casts must not fire it");
    }

    // ── Kicker cost ─────────────────────────────────────────────────────

    [Fact]
    public void Kicker_AltCost_Is4U()
    {
        var card = SeaGateStormcallerFactory.Create(_alice);
        var cost = SeaGateStormcallerFactory.BuildAdditionalCost(card);

        cost.Should().BeOfType<KickerAdditionalCost>();
        var kicker = (KickerAdditionalCost)cost;
        kicker.KickerCost.Generic.Should().Be(4, "Kicker {4}{U}");
        kicker.KickerCost.Blue.Should().Be(1, "Kicker {4}{U}");
        kicker.KickerCost.TotalValue.Should().Be(5);
    }

    // ── Shape-only path ─────────────────────────────────────────────────

    [Fact]
    public void Etb_WithoutTriggerManager_IsShapeOnlyNoOp()
    {
        var card = SeaGateStormcallerFactory.Create(_alice, triggers: null, stack: null);
        var etb = card.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var e in etb.Effects) e.Execute(); };
        act.Should().NotThrow();
    }
}
