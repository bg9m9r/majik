using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// Tests Rule 603 trigger lifecycle: bus subscription, pending queue, APNAP drain.
/// </summary>
public class TriggerManagerTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _manager;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public TriggerManagerTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _manager = new TriggerManager(_stack, _bus);
    }

    [Fact]
    public void Register_Null_Throws()
    {
        _manager.Invoking(m => m.RegisterTriggeredAbility(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EvaluateTriggers_MatchingAbility_EnqueuesPending_DoesNotPushStack()
    {
        var ability = BuildEtbAbility(_alice, out var source);
        source.SetZone(ZoneType.Battlefield);
        _manager.RegisterTriggeredAbility(ability);

        _manager.EvaluateTriggers(new CardMovedEvent(source, ZoneType.Hand, ZoneType.Battlefield));

        _stack.IsEmpty.Should().BeTrue();
        _manager.PendingCount.Should().Be(1);
    }

    [Fact]
    public void EvaluateTriggers_NonMatchingEvent_DoesNotEnqueue()
    {
        var ability = BuildEtbAbility(_alice, out var source);
        source.SetZone(ZoneType.Battlefield);
        _manager.RegisterTriggeredAbility(ability);

        _manager.EvaluateTriggers(new CardDrawnEvent(source, _alice));

        _manager.PendingCount.Should().Be(0);
    }

    [Fact]
    public void EvaluateTriggers_InterveningIfFalse_DoesNotEnqueue()
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Battlefield };
        var ability = new TriggeredAbility(source, _alice,
            Triggers.OnEnterBattlefieldSelf(source),
            interveningIf: () => false);
        _manager.RegisterTriggeredAbility(ability);

        _manager.EvaluateTriggers(new CardMovedEvent(source, ZoneType.Hand, ZoneType.Battlefield));

        _manager.PendingCount.Should().Be(0);
    }

    [Fact]
    public void EvaluateTriggers_NullEvent_NoOp()
    {
        var ability = BuildEtbAbility(_alice, out _);
        _manager.RegisterTriggeredAbility(ability);

        _manager.EvaluateTriggers(null!);

        _manager.PendingCount.Should().Be(0);
    }

    [Fact]
    public void PutPendingTriggersOnStack_ApnapOrders_ThenPushes()
    {
        var aliceAbility = BuildEtbAbility(_alice, out var aliceSrc);
        var bobAbility = BuildEtbAbility(_bob, out var bobSrc);
        aliceSrc.SetZone(ZoneType.Battlefield);
        bobSrc.SetZone(ZoneType.Battlefield);
        _manager.RegisterTriggeredAbility(bobAbility);
        _manager.RegisterTriggeredAbility(aliceAbility);

        _manager.EvaluateTriggers(new CardMovedEvent(bobSrc, ZoneType.Hand, ZoneType.Battlefield));
        _manager.EvaluateTriggers(new CardMovedEvent(aliceSrc, ZoneType.Hand, ZoneType.Battlefield));

        _manager.PutPendingTriggersOnStack(activePlayer: _alice);

        _stack.Count.Should().Be(2);
        _manager.PendingCount.Should().Be(0);
        // APNAP: active (Alice) goes onto stack first → Bob's ability ends up on top
        _stack.Top.Should().BeSameAs(bobAbility);
    }

    [Fact]
    public void EvaluateTriggers_FiresTriggeredAbilityTriggeredEvent()
    {
        TriggeredAbilityTriggeredEvent? captured = null;
        _bus.Subscribe<TriggeredAbilityTriggeredEvent>(e => captured = e);
        var ability = BuildEtbAbility(_alice, out var src);
        src.SetZone(ZoneType.Battlefield);
        _manager.RegisterTriggeredAbility(ability);

        _manager.EvaluateTriggers(new CardMovedEvent(src, ZoneType.Hand, ZoneType.Battlefield));

        captured.Should().NotBeNull();
        captured!.Ability.Should().BeSameAs(ability);
    }

    [Fact]
    public void AutoSubscribe_PublishedEvent_TriggersEvaluation()
    {
        var ability = BuildEtbAbility(_alice, out var src);
        src.SetZone(ZoneType.Battlefield);
        _manager.RegisterTriggeredAbility(ability);

        _bus.Publish(new CardMovedEvent(src, ZoneType.Hand, ZoneType.Battlefield));

        _manager.PendingCount.Should().Be(1);
    }

    [Fact]
    public void BindCard_RegistersTriggeredAbilities_WhenCardEntersActiveZone()
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Hand };
        var ability = new TriggeredAbility(source, _alice, Triggers.OnEnterBattlefieldSelf(source));
        source.AddAbility(ability);
        _manager.BindCard(source);

        // ZoneService updates card.Zone before publishing CardMovedEvent.
        source.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(source, ZoneType.Hand, ZoneType.Battlefield));

        _manager.PendingCount.Should().Be(1);
    }

    [Fact]
    public void BindCard_UnregistersAbilities_WhenCardLeavesAllActiveZones()
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Battlefield };
        var ability = new TriggeredAbility(source, _alice, Triggers.OnEnterBattlefieldSelf(source));
        source.AddAbility(ability);
        _manager.BindCard(source);

        source.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(source, ZoneType.Battlefield, ZoneType.Graveyard));

        _manager.IsRegistered(ability).Should().BeFalse();
    }

    private TriggeredAbility BuildEtbAbility(Player controller, out Creature source)
    {
        source = new Creature($"Bear-{Guid.NewGuid()}", "1G", 2, 2) { Owner = controller };
        return new TriggeredAbility(source, controller, Triggers.OnEnterBattlefieldSelf(source));
    }

    // --- PLAN 05: snapshot re-entrancy (matches old _abilities.ToList()) ---

    [Fact]
    public void EvaluateTriggers_AbilityRegisteredMidLoop_NotEvaluatedForCurrentEvent()
    {
        // A registered ability whose condition predicate registers ANOTHER
        // ability mid-evaluation. The new ability must not be evaluated for
        // the in-flight event (snapshot captured before the loop), but is for
        // the next event — exactly the old `_abilities.ToList()` semantics.
        var lateSource = new Creature("Late", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Battlefield };
        var lateFired = 0;
        var late = new TriggeredAbility(lateSource, _alice,
            new EventTriggerCondition<CardMovedEvent>((_, _) => { lateFired++; return false; }));

        var earlySource = new Creature("Early", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Battlefield };
        var registeredOnce = false;
        var early = new TriggeredAbility(earlySource, _alice,
            new EventTriggerCondition<CardMovedEvent>((_, _) =>
            {
                if (!registeredOnce)
                {
                    registeredOnce = true;
                    _manager.RegisterTriggeredAbility(late);
                }
                return false;
            }));

        _manager.RegisterTriggeredAbility(early);

        _manager.EvaluateTriggers(new CardMovedEvent(earlySource, ZoneType.Hand, ZoneType.Battlefield));
        // 'late' was registered DURING this evaluation → not seen this event.
        lateFired.Should().Be(0);

        _manager.EvaluateTriggers(new CardMovedEvent(earlySource, ZoneType.Hand, ZoneType.Battlefield));
        // Next event sees the rebuilt snapshot including 'late'.
        lateFired.Should().Be(1);
    }

    [Fact]
    public void EvaluateTriggers_AbilityUnregisteredMidLoop_StillEvaluatedThisEvent_GoneNext()
    {
        // 'first' unregisters 'second' mid-loop. Because the snapshot was
        // captured before the loop, 'second' still evaluates this event; it is
        // absent on the next event.
        var secondSource = new Creature("Second", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Battlefield };
        var secondEvaluated = 0;
        var second = new TriggeredAbility(secondSource, _alice,
            new EventTriggerCondition<CardMovedEvent>((_, _) => { secondEvaluated++; return false; }));

        var firstSource = new Creature("First", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Battlefield };
        var first = new TriggeredAbility(firstSource, _alice,
            new EventTriggerCondition<CardMovedEvent>((_, _) =>
            {
                _manager.UnregisterTriggeredAbility(second);
                return false;
            }));

        // Register 'first' before 'second' so it runs first in the snapshot.
        _manager.RegisterTriggeredAbility(first);
        _manager.RegisterTriggeredAbility(second);

        _manager.EvaluateTriggers(new CardMovedEvent(firstSource, ZoneType.Hand, ZoneType.Battlefield));
        secondEvaluated.Should().Be(1); // still in the captured snapshot

        _manager.EvaluateTriggers(new CardMovedEvent(firstSource, ZoneType.Hand, ZoneType.Battlefield));
        secondEvaluated.Should().Be(1); // removed before this event's snapshot
    }

    [Fact]
    public void EvaluateTriggers_DelayedTriggerSelfRemoves_FiresExactlyOnce()
    {
        // Delayed triggers auto-unregister after firing. With the snapshot
        // cache this must still fire exactly once across repeated events.
        var src = new Creature("Delayed", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Battlefield };
        var fired = 0;
        var delayed = new DelayedTriggeredAbility(src, _alice,
            new EventTriggerCondition<CardMovedEvent>((_, _) => { fired++; return true; }));
        _manager.RegisterDelayed(delayed);

        _manager.EvaluateTriggers(new CardMovedEvent(src, ZoneType.Hand, ZoneType.Battlefield));
        _manager.EvaluateTriggers(new CardMovedEvent(src, ZoneType.Hand, ZoneType.Battlefield));

        fired.Should().Be(1);
        _manager.PendingCount.Should().Be(1);
    }
}
