using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Spellgorger Weird (Guilds of Ravnica, {2}{R}).
///
/// Oracle (Scryfall, verified):
///   Creature — Weird, 2/2.
///   "Whenever you cast a noncreature spell, put a +1/+1 counter on this
///    creature."
///
/// Covers:
///   - Card shape (name, type, subtype, P/T, mana cost, owner/controller).
///   - Cast trigger fires on a controller's noncreature spell (counter goes
///     down — CR 603.1).
///   - Cast trigger does NOT fire on a creature spell.
///   - Cast trigger does NOT fire on an opponent's noncreature spell
///     (CR 109.5 — "you cast").
///   - Multiple noncreature casts stack counters.
///   - NamedCardFactory dispatch.
/// </summary>
public class SpellgorgerWeirdFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Shock")
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewSorcerySpell(Player controller, string name = "Lava Spike")
    {
        var sorcery = new Sorcery(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(sorcery, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller)
    {
        var creature = new Creature("Bear", "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    [Fact]
    public void SpellgorgerWeird_IsWeird_2_2_AtCost2R()
    {
        var c = SpellgorgerWeirdFactory.Create(_alice);

        c.Name.Should().Be("Spellgorger Weird");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Weird).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpellgorgerWeird_CastNoncreatureSpell_QueuesTrigger_AndAddsCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var weird = SpellgorgerWeirdFactory.Create(_alice, bus, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(weird);
        weird.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice)));

        triggers.PendingCount.Should().Be(1,
            "casting a noncreature spell fires Spellgorger Weird's trigger (CR 603.1)");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        weird.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void SpellgorgerWeird_CastSorcery_AlsoTriggers()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var weird = SpellgorgerWeirdFactory.Create(_alice, bus, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(weird);
        weird.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice)));

        triggers.PendingCount.Should().Be(1, "a sorcery is a noncreature spell");
    }

    [Fact]
    public void SpellgorgerWeird_CastCreatureSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var weird = SpellgorgerWeirdFactory.Create(_alice, bus, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(weird);
        weird.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice)));

        triggers.PendingCount.Should().Be(0, "a creature spell is not a noncreature spell");
        weird.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void SpellgorgerWeird_OpponentCastsNoncreatureSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var weird = SpellgorgerWeirdFactory.Create(_alice, bus, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(weird);
        weird.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob)));

        triggers.PendingCount.Should().Be(0,
            "'whenever YOU cast' is controller-scoped (CR 109.5)");
        weird.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void SpellgorgerWeird_MultipleNoncreatureCasts_StackCounters()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var weird = SpellgorgerWeirdFactory.Create(_alice, bus, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(weird);
        weird.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Shock")));
        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Lava Spike")));
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Opt")));

        triggers.PendingCount.Should().Be(3);
        triggers.PutPendingTriggersOnStack(_alice);
        while (true)
        {
            var top = stack.Pop();
            if (top == null) break;
            top.Resolve();
        }

        weird.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "each noncreature cast lands an independent +1/+1 counter");
    }

    [Fact]
    public void SpellgorgerWeird_NamedCardFactory_Dispatch()
    {
        var card = NamedCardFactory.Create("Spellgorger Weird", _alice);

        card.Should().NotBeNull();
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Spellgorger Weird");
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(2);
        card.HasSubtype(CardSubtype.Weird).Should().BeTrue();
    }
}
