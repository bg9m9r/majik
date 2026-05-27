using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="KambalConsulOfAllocationFactory"/> — Legendary
/// Creature {1}{W}{B} (Kaladesh).
///
/// Oracle: "Whenever an opponent casts a noncreature spell, that player
/// loses 2 life and you gain 2 life."
///
/// Covers:
/// - Identity (Legendary Human Advisor 2/3 at {1}{W}{B}).
/// - NamedCardFactory dispatch.
/// - Trigger fires on opponent's noncreature spell.
/// - Trigger ignores opponent's creature spells.
/// - Trigger ignores controller's own casts.
/// - Resolution: opponent loses 2 (LifeLostThisTurn ticks), controller
///   gains 2.
/// - Trigger only active on the battlefield.
/// </summary>
public class KambalConsulOfAllocationTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewInstant(Player controller, string name, string manaCost)
    {
        var c = new Instant(name, manaCost) { Owner = controller };
        return new Majik.Core.Spells.Spell(c, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name, string manaCost)
    {
        var c = new Creature(name, manaCost: manaCost, power: 1, toughness: 1) { Owner = controller };
        return new Majik.Core.Spells.Spell(c, controller);
    }

    private static void PlaceOnBattlefield(Player controller, Creature card)
    {
        controller.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Kambal_Identity_LegendaryHumanAdvisor_2_3_AtCost1WB()
    {
        var k = KambalConsulOfAllocationFactory.Create(_alice);

        k.Name.Should().Be("Kambal, Consul of Allocation");
        k.ManaCost.Should().Be("{1}{W}{B}");
        k.HasType(CardType.Creature).Should().BeTrue();
        k.Supertypes.Should().Contain(CardSupertype.Legendary);
        k.HasSubtype(CardSubtype.Human).Should().BeTrue();
        k.HasSubtype(CardSubtype.Advisor).Should().BeTrue();
        k.BasePower.Should().Be(2);
        k.BaseToughness.Should().Be(3);
        k.Owner.Should().BeSameAs(_alice);
        k.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Kambal_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Kambal, Consul of Allocation", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Kambal, Consul of Allocation");
        c.HasSubtype(CardSubtype.Advisor).Should().BeTrue();
    }

    [Fact]
    public void Kambal_HasSingleTriggeredAbility()
    {
        var k = KambalConsulOfAllocationFactory.Create(_alice);
        k.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Trigger predicate + resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentCastsNoncreatureSpell_TriggersAndDrains()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var k = KambalConsulOfAllocationFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, k);

        bus.Publish(new SpellCastEvent(NewInstant(_bob, "Lightning Bolt", "{R}")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(18, "Bob loses 2 to Kambal");
        _bob.LifeLostThisTurn.Should().Be(2,
            "the loss feeds spectacle / revolt / lifegain observers");
        _alice.LifeTotal.Should().Be(22, "Alice gains 2");
    }

    [Fact]
    public void OpponentCastsCreatureSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var k = KambalConsulOfAllocationFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, k);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_bob, "Bear", "{1}{G}")));

        triggers.PendingCount.Should().Be(0,
            "printed text gates on 'noncreature spell'");
    }

    [Fact]
    public void ControllerCastsNoncreatureSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var k = KambalConsulOfAllocationFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, k);

        bus.Publish(new SpellCastEvent(NewInstant(_alice, "Own Bolt", "{R}")));

        triggers.PendingCount.Should().Be(0,
            "printed text gates on 'an opponent casts' — controller's own casts don't trigger");
    }

    [Fact]
    public void Trigger_OnlyActiveOnBattlefield()
    {
        var k = KambalConsulOfAllocationFactory.Create(_alice);
        var trigger = k.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().BeEquivalentTo(new[] { ZoneType.Battlefield },
            "CR 603.6 — Kambal's trigger functions while on the battlefield");
    }
}
