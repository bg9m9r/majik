using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Events;

/// <summary>
/// PLAN 05 — re-entrancy + typed-routing invariants for the copy-on-write
/// <see cref="EventBus"/>. These lock the exact semantics the old per-publish
/// <c>.ToArray()</c> snapshot provided: a handler subscribed or unsubscribed
/// DURING a publish is not observed by the in-flight loop, but is observed by
/// the next publish.
/// </summary>
public class EventBusCopyOnWriteTests
{
    private static Creature DummyCreature() => new("Dummy", "{0}", 1, 1);

    private static CardMovedEvent Moved() =>
        new(DummyCreature(), ZoneType.Hand, ZoneType.Battlefield);

    [Fact]
    public void Publish_HandlerSubscribesAnother_NewHandlerNotInvokedForCurrentEvent()
    {
        var bus = new EventBus();
        var inner = 0;
        Action<GameStartedEvent> innerHandler = _ => inner++;

        var outerCalls = 0;
        bus.Subscribe<GameStartedEvent>(_ =>
        {
            outerCalls++;
            // Subscribe a brand-new handler mid-dispatch.
            if (outerCalls == 1) bus.Subscribe(innerHandler);
        });

        bus.Publish(new GameStartedEvent());

        // The newly-subscribed handler must NOT see the in-flight event
        // (matches old .ToArray() snapshot semantics).
        inner.Should().Be(0);

        // But it IS wired for the next publish.
        bus.Publish(new GameStartedEvent());
        inner.Should().Be(1);
    }

    [Fact]
    public void Publish_HandlerUnsubscribesAlreadySnapshotted_StillInvokedThisEvent()
    {
        var bus = new EventBus();
        var firstCalls = 0;
        var secondCalls = 0;

        Action<GameStartedEvent> second = _ => secondCalls++;

        // First handler removes the second (already-snapshotted) handler.
        bus.Subscribe<GameStartedEvent>(_ =>
        {
            firstCalls++;
            bus.Unsubscribe(second);
        });
        bus.Subscribe(second);

        bus.Publish(new GameStartedEvent());

        // The second handler was captured in the snapshot before the first
        // ran, so it still fires for THIS event (old .ToArray() semantics).
        firstCalls.Should().Be(1);
        secondCalls.Should().Be(1);

        // On the next publish it is gone.
        bus.Publish(new GameStartedEvent());
        firstCalls.Should().Be(2);
        secondCalls.Should().Be(1);
    }

    [Fact]
    public void Publish_GlobalHandlerUnsubscribesItself_StillInvokedThisEvent()
    {
        var bus = new EventBus();
        var calls = 0;
        Action<GameEvent> self = null!;
        self = _ =>
        {
            calls++;
            bus.UnsubscribeAll(self); // one-shot self-removal (cleanup-handler pattern)
        };
        bus.SubscribeAll(self);

        bus.Publish(new GameStartedEvent());
        bus.Publish(new GameStartedEvent());

        // Fires exactly once: the self-unsubscribe takes effect for the
        // NEXT publish, never re-running for the current snapshot.
        calls.Should().Be(1);
    }

    [Fact]
    public void Subscribe_TypedHandler_RoutesOnlyForExactEventType()
    {
        var bus = new EventBus();
        var moved = 0;
        var started = 0;
        bus.Subscribe<CardMovedEvent>(_ => moved++);
        bus.Subscribe<GameStartedEvent>(_ => started++);

        bus.Publish(Moved());

        moved.Should().Be(1);
        started.Should().Be(0); // unrelated typed channel not invoked
    }

    [Fact]
    public void Subscribe_BaseType_DoesNotReceiveDerivedEvent_ExactTypeDispatchPreserved()
    {
        // CR/engine contract (SengirVampire / Stormscale rely on this):
        // dispatch keys on the STATIC published type, not the runtime
        // hierarchy. A Subscribe<DamageDealtEvent> handler must NOT fire for
        // a published CombatDamageDealtEvent (a derived type), and vice versa.
        var bus = new EventBus();
        var baseCalls = 0;
        var derivedCalls = 0;
        bus.Subscribe<DamageDealtEvent>(_ => baseCalls++);
        bus.Subscribe<CombatDamageDealtEvent>(_ => derivedCalls++);

        bus.Publish(new CombatDamageDealtEvent(DummyCreature(), (ICard)DummyCreature(), 1));

        baseCalls.Should().Be(0);   // base subscription NOT walked from derived
        derivedCalls.Should().Be(1);
    }

    [Fact]
    public void SubscribeAll_GlobalHandler_ReceivesEveryEventType()
    {
        var bus = new EventBus();
        var seen = 0;
        bus.SubscribeAll(_ => seen++);

        bus.Publish(new GameStartedEvent());
        bus.Publish(Moved());

        seen.Should().Be(2);
    }

    [Fact]
    public void Publish_ManyHandlers_NoStructuralModificationException_DuringReentrantChurn()
    {
        // Stress the COW store: handlers subscribe/unsubscribe siblings while a
        // publish iterates the captured snapshot. Must never throw.
        var bus = new EventBus();
        for (var i = 0; i < 20; i++)
        {
            Action<GameStartedEvent> h = null!;
            h = _ =>
            {
                bus.Subscribe<GameStartedEvent>(__ => { });
                bus.Unsubscribe(h);
            };
            bus.Subscribe(h);
        }

        var act = () =>
        {
            bus.Publish(new GameStartedEvent());
            bus.Publish(new GameStartedEvent());
        };

        act.Should().NotThrow();
    }
}
