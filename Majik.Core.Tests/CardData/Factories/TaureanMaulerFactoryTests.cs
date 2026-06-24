using FluentAssertions;
using Majik.Core.Abilities;
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
/// Unit tests for <see cref="TaureanMaulerFactory"/> (Time Spiral, {2}{R}).
///
/// Covers the card's UNIQUE behaviour:
/// - Identity ({2}{R}, Creature — Shapeshifter, 2/2).
/// - Changeling (CR 702.73) — the card is every creature type (spot-check a
///   couple of unrelated tribes) plus the printed Changeling keyword marker.
/// - Opponent-cast growth trigger (CR 603.6a / 109.5): the condition matches a
///   spell cast by an OPPONENT only — NOT the controller's own cast.
/// - Resolution places exactly one +1/+1 counter on this creature.
/// - Trigger active only on the battlefield (CR 113.6).
/// </summary>
[Trait("Color", "R")]
public class TaureanMaulerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewSorcerySpell(Player controller, string name = "Bolt")
    {
        var sorcery = new Sorcery(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(sorcery, controller);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void TaureanMauler_Identity()
    {
        var c = TaureanMaulerFactory.Create(_alice);

        c.Name.Should().Be("Taurean Mauler");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shapeshifter).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Changeling — CR 702.73a (the card is every creature type)
    // -----------------------------------------------------------------------

    [Fact]
    public void TaureanMauler_Changeling_IsEveryCreatureType()
    {
        var c = TaureanMaulerFactory.Create(_alice);

        // Spot-check unrelated tribes — Changeling means it is each of them.
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue("CR 702.73a — Changeling is every creature type");
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue("CR 702.73a — Changeling is every creature type");

        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Changeling", "CR 702.73 — Changeling is printed on Taurean Mauler");
    }

    // -----------------------------------------------------------------------
    // Opponent-cast growth trigger condition (CR 109.5)
    // -----------------------------------------------------------------------

    [Fact]
    public void GrowTrigger_FiresForOpponentCast_NotControllerCast()
    {
        var c = TaureanMaulerFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        // An opponent (Bob) casting a spell fires it (CR 109.5).
        trigger.Condition.Matches(new SpellCastEvent(NewSorcerySpell(_bob)), trigger).Should().BeTrue();
        // The controller's OWN cast does NOT (you are not your own opponent).
        trigger.Condition.Matches(new SpellCastEvent(NewSorcerySpell(_alice)), trigger).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Counter resolution — one +1/+1 counter
    // -----------------------------------------------------------------------

    [Fact]
    public void TaureanMauler_OpponentCast_PutsOneCounter()
    {
        var c = TaureanMaulerFactory.Create(_alice, triggers: null);

        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "put a +1/+1 counter on Taurean Mauler");
    }

    // -----------------------------------------------------------------------
    // End-to-end: bus-fired opponent cast auto-queues + resolves the trigger.
    // -----------------------------------------------------------------------

    [Fact]
    public void TaureanMauler_BusFiredOpponentCast_QueuesAndResolves()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var c = TaureanMaulerFactory.Create(_alice, triggers: triggers);

        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_bob)));

        triggers.PendingCount.Should().Be(1, "an opponent casting a spell queues the trigger");
        triggers.PutPendingTriggersOnStack(_alice);
        while (true)
        {
            var top = stack.Pop();
            if (top == null) break;
            top.Resolve();
        }

        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "opponent cast a spell → one +1/+1 counter placed on resolution");
    }

    [Fact]
    public void TaureanMauler_ControllerCast_DoesNotQueue()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var c = TaureanMaulerFactory.Create(_alice, triggers: triggers);

        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        // The CONTROLLER casting a spell must not queue (CR 109.5).
        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice)));

        triggers.PendingCount.Should().Be(0,
            "the controller's own cast is not 'an opponent casts a spell'");
    }

    [Fact]
    public void TaureanMauler_GrowTrigger_OnlyActiveOnBattlefield()
    {
        var c = TaureanMaulerFactory.Create(_alice);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }
}
