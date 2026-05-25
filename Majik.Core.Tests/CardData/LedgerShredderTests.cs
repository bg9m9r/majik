using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Ledger Shredder (Streets of New Capenna, {1}{U}).
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch hands back the same shape.
///   - Flying keyword.
///   - Two triggered abilities present on the card.
///   - Mechanic: the controller's first spell each turn does not trigger
///     surveil; the second does — top library card → graveyard, +1/+1
///     counter on Ledger Shredder.
///   - Mechanic: the controller's third spell does not retrigger.
///   - Mechanic: a TurnStartedEvent resets the per-turn count so the next
///     turn's second spell triggers again.
/// </summary>
public class LedgerShredderTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Card NewCardInLibrary(Player owner, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static Majik.Core.Spells.Spell NewSpell(Player controller, string name = "Spark")
    {
        var instant = new Instant(name, "U") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    [Fact]
    public void LedgerShredder_Identity_BirdAdvisor_1_3_AtCost1U()
    {
        var ls = LedgerShredderFactory.Create(_alice);

        ls.Name.Should().Be("Ledger Shredder");
        ls.ManaCost.Should().Be("{1}{U}");
        ls.HasType(CardType.Creature).Should().BeTrue();
        ls.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        ls.HasSubtype(CardSubtype.Advisor).Should().BeTrue();
        ls.BasePower.Should().Be(1);
        ls.BaseToughness.Should().Be(3);
        ls.Owner.Should().BeSameAs(_alice);
        ls.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LedgerShredder_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Ledger Shredder", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Ledger Shredder");
        card.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        card.HasSubtype(CardSubtype.Advisor).Should().BeTrue();
    }

    [Fact]
    public void LedgerShredder_HasFlying_AndTwoTriggeredAbilities()
    {
        var ls = LedgerShredderFactory.Create(_alice);

        CombatAbilities.HasFlying(ls).Should().BeTrue();
        ls.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void FirstSpellCast_DoesNotTrigger_NoSurveil_NoCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ls = LedgerShredderFactory.Create(_alice, bus, triggers);
        ls.SetZone(ZoneType.Battlefield);

        var libTop = NewCardInLibrary(_alice, "Top");

        bus.Publish(new SpellCastEvent(NewSpell(_alice)));

        triggers.PendingCount.Should().Be(0);
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().Equal(new[] { libTop });
        ls.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void SecondSpellCast_Triggers_SurveilsOneAndAddsCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ls = LedgerShredderFactory.Create(_alice, bus, triggers);
        ls.SetZone(ZoneType.Battlefield);

        var top = NewCardInLibrary(_alice, "Top");
        var next = NewCardInLibrary(_alice, "Next");

        // First spell — no trigger.
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "First")));
        triggers.PendingCount.Should().Be(0);

        // Second spell — surveil 1 + +1/+1 counter.
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "Second")));
        triggers.PendingCount.Should().Be(1);

        // Resolve the surveil trigger; its body publishes SurveilEvent
        // which queues the self-trigger (Triggers.OnSurveil) as pending.
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // SurveilEvent → self-trigger pending. Resolve it for the counter.
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // No agent registered → surveiled card goes to graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { next });
        ls.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void ThirdSpellCast_DoesNotRetrigger_OnlyTheSecondFires()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ls = LedgerShredderFactory.Create(_alice, bus, triggers);
        ls.SetZone(ZoneType.Battlefield);

        var top1 = NewCardInLibrary(_alice, "Top1");
        var top2 = NewCardInLibrary(_alice, "Top2");

        bus.Publish(new SpellCastEvent(NewSpell(_alice, "S1"))); // no trigger
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "S2"))); // triggers
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();
        // Drain the chained self-surveil trigger (+1/+1 counter).
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bus.Publish(new SpellCastEvent(NewSpell(_alice, "S3"))); // must not retrigger
        triggers.PendingCount.Should().Be(0);

        // Library / graveyard reflect exactly one surveil.
        _alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top1 });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { top2 });
        ls.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void TurnBoundary_ResetsCount_NextTurnSecondSpellTriggersAgain()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ls = LedgerShredderFactory.Create(_alice, bus, triggers);
        ls.SetZone(ZoneType.Battlefield);

        var t1Top = NewCardInLibrary(_alice, "T1Top");
        var t2Top = NewCardInLibrary(_alice, "T2Top");

        // Turn 1 — cast two spells, second triggers.
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "T1S1")));
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "T1S2")));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();
        // Drain self-surveil chain.
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();
        ls.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);

        // Turn boundary — fires TurnStartedEvent, reset closure count.
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        // Turn 2 — only the second spell triggers again.
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "T2S1")));
        triggers.PendingCount.Should().Be(0);

        bus.Publish(new SpellCastEvent(NewSpell(_alice, "T2S2")));
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();
        // Drain self-surveil chain.
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        ls.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
        _alice.Zones.Graveyard.GetCards().Should().Equal(new[] { t1Top, t2Top });
    }

    [Fact]
    public void OpponentSpellCast_DoesNotIncrementControllerCount()
    {
        var bob = new Player("Bob", 20);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ls = LedgerShredderFactory.Create(_alice, bus, triggers);
        ls.SetZone(ZoneType.Battlefield);

        NewCardInLibrary(_alice, "Top");

        // Opponent (Bob) casts two spells — neither should bump Alice's count.
        bus.Publish(new SpellCastEvent(NewSpell(bob, "BobS1")));
        bus.Publish(new SpellCastEvent(NewSpell(bob, "BobS2")));
        triggers.PendingCount.Should().Be(0);

        // Alice's first spell — still no trigger.
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "S1")));
        triggers.PendingCount.Should().Be(0);

        // Alice's second spell — triggers.
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "S2")));
        triggers.PendingCount.Should().Be(1);
    }
}
