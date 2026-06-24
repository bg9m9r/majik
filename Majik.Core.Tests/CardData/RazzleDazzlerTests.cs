using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Razzle-Dazzler (Bloomburrow, {1}{U}).
///
/// Covers the card's UNIQUE behaviour:
///   - Card identity (name, type, subtypes, P/T, mana cost) — non-vanilla.
///   - Mechanic: the controller's first spell each turn does not trigger;
///     the second does — a +1/+1 counter is placed on Razzle-Dazzler and it
///     gains "can't be blocked this turn" (CR 509.1c, CR 514.2).
///   - Mechanic: the controller's third spell does not retrigger.
///   - Mechanic: a TurnStartedEvent resets the per-turn count.
///   - Mechanic: an opponent's spells do not increment the controller's count.
///
/// NamedCardFactory dispatch + well-formedness are asserted globally by
/// CardFactoryContractTests, so no dispatch test lives here.
/// </summary>
[Trait("Color", "U")]
public class RazzleDazzlerTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Majik.Core.Spells.Spell NewSpell(Player controller, string name = "Spark")
    {
        var instant = new Instant(name, "U") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Creature OnBattlefield(Creature c)
    {
        // The can't-be-blocked grant is registered on the card's own
        // ActiveEffects (null in pure-shape construction). Give it a live
        // service so the combat restriction can be observed.
        c.ActiveEffects = new ContinuousEffectsService();
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    [Fact]
    public void RazzleDazzler_Identity_HumanWizard_1_2_AtCost1U()
    {
        var rd = RazzleDazzlerFactory.Create(_alice);

        rd.Name.Should().Be("Razzle-Dazzler");
        rd.ManaCost.Should().Be("{1}{U}");
        rd.HasType(CardType.Creature).Should().BeTrue();
        rd.HasSubtype(CardSubtype.Human).Should().BeTrue();
        rd.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        rd.BasePower.Should().Be(1);
        rd.BaseToughness.Should().Be(2);
        rd.Owner.Should().BeSameAs(_alice);
        rd.Controller.Should().BeSameAs(_alice);
        rd.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void FirstSpellCast_DoesNotTrigger_NoCounter_NoUnblockable()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var rd = OnBattlefield(RazzleDazzlerFactory.Create(_alice, bus, triggers));

        bus.Publish(new SpellCastEvent(NewSpell(_alice)));

        triggers.PendingCount.Should().Be(0);
        rd.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        rd.ActiveEffects!.HasRestriction(rd, CombatRestriction.CannotBeBlocked).Should().BeFalse();
    }

    [Fact]
    public void SecondSpellCast_Triggers_AddsCounter_AndGrantsCantBeBlocked()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var rd = OnBattlefield(RazzleDazzlerFactory.Create(_alice, bus, triggers));

        // First spell — no trigger.
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "First")));
        triggers.PendingCount.Should().Be(0);

        // Second spell — +1/+1 counter + can't be blocked this turn.
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "Second")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        rd.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        rd.ActiveEffects!.HasRestriction(rd, CombatRestriction.CannotBeBlocked).Should().BeTrue();
    }

    [Fact]
    public void ThirdSpellCast_DoesNotRetrigger_OnlyTheSecondFires()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var rd = OnBattlefield(RazzleDazzlerFactory.Create(_alice, bus, triggers));

        bus.Publish(new SpellCastEvent(NewSpell(_alice, "S1"))); // no trigger
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "S2"))); // triggers
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bus.Publish(new SpellCastEvent(NewSpell(_alice, "S3"))); // must not retrigger
        triggers.PendingCount.Should().Be(0);

        rd.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void TurnBoundary_ResetsCount_NextTurnSecondSpellTriggersAgain()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var rd = OnBattlefield(RazzleDazzlerFactory.Create(_alice, bus, triggers));

        // Turn 1 — cast two spells, second triggers.
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "T1S1")));
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "T1S2")));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();
        rd.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);

        // Turn boundary — fires TurnStartedEvent, resets closure count.
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        // Turn 2 — only the second spell triggers again.
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "T2S1")));
        triggers.PendingCount.Should().Be(0);

        bus.Publish(new SpellCastEvent(NewSpell(_alice, "T2S2")));
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        rd.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    [Fact]
    public void OpponentSpellCast_DoesNotIncrementControllerCount()
    {
        var bob = new Player("Bob", 20);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var rd = OnBattlefield(RazzleDazzlerFactory.Create(_alice, bus, triggers));

        // Opponent (Bob) casts two spells — neither bumps Alice's count.
        bus.Publish(new SpellCastEvent(NewSpell(bob, "BobS1")));
        bus.Publish(new SpellCastEvent(NewSpell(bob, "BobS2")));
        triggers.PendingCount.Should().Be(0);

        bus.Publish(new SpellCastEvent(NewSpell(_alice, "S1"))); // first — no trigger
        triggers.PendingCount.Should().Be(0);

        bus.Publish(new SpellCastEvent(NewSpell(_alice, "S2"))); // second — triggers
        triggers.PendingCount.Should().Be(1);
    }
}
