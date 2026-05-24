using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="EsperSentinelFactory"/> (Modern Horizons 2,
/// {W}). Oracle: "Whenever an opponent casts their first noncreature
/// spell each turn, unless they pay {X}, where X is the number of
/// creatures you control, you draw a card."
///
/// Covers:
/// - Identity (Human Soldier 1/1, mana cost {W}, owner/controller).
/// - NamedCardFactory dispatch.
/// - Opponent's first noncreature spell triggers → controller draws when
///   the opponent can't pay {X}.
/// - Opponent's second noncreature spell same turn does NOT trigger
///   (CR 603.1 — "first noncreature spell each turn").
/// - Opponent's creature spell does NOT trigger.
/// - Controller's own spell does NOT trigger ("opponent casts").
/// - Opponent pays {X} from their mana pool → no draw.
/// - X scales with the controller's creature count on the battlefield.
/// - TurnStartedEvent resets the per-opponent count.
/// </summary>
public class EsperSentinelTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Card NewCardInLibrary(Player owner, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static Majik.Core.Spells.Spell NewNoncreatureSpell(Player controller, string name = "Lightning Bolt")
    {
        var inst = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(inst, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Grizzly Bears")
    {
        var crt = new Creature(name, "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(crt, controller);
    }

    private static Creature NewBattlefieldCreature(Player controller, string name)
    {
        var crt = new Creature(name, "G", 1, 1);
        crt.SetOwner(controller);
        crt.SetController(controller);
        controller.Zones.Battlefield.AddCard(crt);
        crt.SetZone(ZoneType.Battlefield);
        return crt;
    }

    private static void PlaceOnBattlefield(Player controller, Creature card)
    {
        controller.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    // -------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------

    [Fact]
    public void EsperSentinel_Identity_HumanSoldier_1_1_AtCostW()
    {
        var es = EsperSentinelFactory.Create(_alice);

        es.Name.Should().Be("Esper Sentinel");
        es.ManaCost.Should().Be("{W}");
        es.HasType(CardType.Creature).Should().BeTrue();
        es.HasSubtype(CardSubtype.Human).Should().BeTrue();
        es.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        es.BasePower.Should().Be(1);
        es.BaseToughness.Should().Be(1);
        es.Owner.Should().BeSameAs(_alice);
        es.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EsperSentinel_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Esper Sentinel", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Esper Sentinel");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
    }

    // -------------------------------------------------------------------
    // Trigger behaviour
    // -------------------------------------------------------------------

    [Fact]
    public void OpponentFirstNoncreatureSpell_OpponentCantPay_ControllerDraws()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var es = EsperSentinelFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(_alice, es);

        // X = 1 (Sentinel itself), Bob has no mana → he declines, Alice draws.
        var top = NewCardInLibrary(_alice, "Top");
        var rest = NewCardInLibrary(_alice, "Rest");

        bus.Publish(new SpellCastEvent(NewNoncreatureSpell(_bob, "BobBolt")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top, "controller draws 1 when opponent can't pay {X}");
        _alice.Zones.Library.GetCards().Should().Equal(new[] { rest });
    }

    [Fact]
    public void OpponentSecondNoncreatureSpellSameTurn_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var es = EsperSentinelFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(_alice, es);

        NewCardInLibrary(_alice, "Top");

        // First spell triggers.
        bus.Publish(new SpellCastEvent(NewNoncreatureSpell(_bob, "BobBolt1")));
        triggers.PendingCount.Should().Be(1);
        // Drain the pending trigger so we observe only retriggers afterward.
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Second spell same turn — does NOT retrigger.
        bus.Publish(new SpellCastEvent(NewNoncreatureSpell(_bob, "BobBolt2")));
        triggers.PendingCount.Should().Be(0,
            "CR 603.1 — 'first noncreature spell each turn' fires once per turn per opponent");
    }

    [Fact]
    public void OpponentCreatureSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var es = EsperSentinelFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(_alice, es);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_bob, "Bears")));

        triggers.PendingCount.Should().Be(0, "creature spells are excluded by oracle text");
    }

    [Fact]
    public void ControllerOwnSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var es = EsperSentinelFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(_alice, es);

        bus.Publish(new SpellCastEvent(NewNoncreatureSpell(_alice, "AliceBolt")));

        triggers.PendingCount.Should().Be(0, "'an opponent casts' excludes the controller's own spells");
    }

    [Fact]
    public void OpponentPaysX_NoDraw()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var es = EsperSentinelFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(_alice, es);

        var top = NewCardInLibrary(_alice, "Top");

        // X = 1 (Sentinel itself). Stock Bob's pool with 1 generic-payable mana.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(1));

        bus.Publish(new SpellCastEvent(NewNoncreatureSpell(_bob, "BobBolt")));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().NotContain(top, "Bob paid {1} so no draw");
        _alice.Zones.Library.GetCards().Should().Contain(top);
    }

    [Fact]
    public void XScalesWithControllerCreatureCount()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var es = EsperSentinelFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(_alice, es);
        NewBattlefieldCreature(_alice, "Friend1");
        NewBattlefieldCreature(_alice, "Friend2");
        // X is now 3 (Sentinel + Friend1 + Friend2).

        var top = NewCardInLibrary(_alice, "Top");

        // Bob has only 2 mana in pool — cannot pay {3}.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(2));

        bus.Publish(new SpellCastEvent(NewNoncreatureSpell(_bob, "BobBolt")));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top,
            "X = 3 (3 controller creatures), Bob has only {2}; controller draws");
    }

    [Fact]
    public void TurnBoundary_ResetsPerOpponentCount()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var es = EsperSentinelFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(_alice, es);

        NewCardInLibrary(_alice, "T1Top");
        NewCardInLibrary(_alice, "T2Top");

        // Turn 1 — Bob's first spell triggers, second doesn't.
        bus.Publish(new SpellCastEvent(NewNoncreatureSpell(_bob, "T1S1")));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bus.Publish(new SpellCastEvent(NewNoncreatureSpell(_bob, "T1S2")));
        triggers.PendingCount.Should().Be(0);

        // Turn boundary.
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        // Turn 2 — Bob's first spell triggers again.
        bus.Publish(new SpellCastEvent(NewNoncreatureSpell(_bob, "T2S1")));
        triggers.PendingCount.Should().Be(1, "per-turn closure resets on TurnStartedEvent");
    }

    [Fact]
    public void Trigger_OnlyActiveOnBattlefield()
    {
        var es = EsperSentinelFactory.Create(_alice);
        var trigger = es.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }
}
