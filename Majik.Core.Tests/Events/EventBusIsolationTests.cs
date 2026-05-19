using FluentAssertions;
using Majik.Core.Events;
using Xunit;

namespace Majik.Core.Tests.Events;

/// <summary>
/// Verifies the EventBus isolation + async contract added in Phase 2
/// slice 4. A throwing handler must never abort the publish loop; async
/// handlers must be awaited under PublishAsync.
/// </summary>
public class EventBusIsolationTests
{
    private sealed class TestEvent : GameEvent
    {
        public string Payload { get; }
        public TestEvent(string payload) : base(EventType.GameStarted) { Payload = payload; }
    }

    [Fact]
    public void Publish_ThrowingHandler_DoesNotAbortOtherHandlers()
    {
        var bus = new EventBus();
        var observed = new List<string>();
        bus.Subscribe<TestEvent>(_ => throw new InvalidOperationException("bad subscriber"));
        bus.Subscribe<TestEvent>(e => observed.Add(e.Payload));

        bus.Publish(new TestEvent("hello"));

        observed.Should().ContainSingle().Which.Should().Be("hello");
    }

    [Fact]
    public void Publish_ThrowingHandler_InvokesOnHandlerError()
    {
        var bus = new EventBus();
        Exception? captured = null;
        bus.OnHandlerError = (ex, _) => captured = ex;
        bus.Subscribe<TestEvent>(_ => throw new InvalidOperationException("boom"));

        bus.Publish(new TestEvent("x"));

        captured.Should().BeOfType<InvalidOperationException>();
        captured!.Message.Should().Be("boom");
    }

    [Fact]
    public async Task PublishAsync_AwaitsAsyncHandlers_AndContinuesAfterFailure()
    {
        var bus = new EventBus();
        var sawFirst = false;
        var sawSecond = false;
        bus.Subscribe<TestEvent>(async e => { await Task.Yield(); sawFirst = true; throw new Exception("bad async"); });
        bus.Subscribe<TestEvent>(async e => { await Task.Yield(); sawSecond = true; });

        await bus.PublishAsync(new TestEvent("y"));

        sawFirst.Should().BeTrue();
        sawSecond.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_RunsSyncAndAsyncHandlers_BothExecuteExactlyOnce()
    {
        var bus = new EventBus();
        var syncHits = 0;
        var asyncHits = 0;
        bus.Subscribe<TestEvent>(_ => Interlocked.Increment(ref syncHits));
        bus.Subscribe<TestEvent>(async _ => { await Task.Yield(); Interlocked.Increment(ref asyncHits); });

        await bus.PublishAsync(new TestEvent("z"));

        syncHits.Should().Be(1);
        asyncHits.Should().Be(1);
    }

    [Fact]
    public void SubscribeAll_ThrowingGlobalHandler_DoesNotAbortOthers()
    {
        var bus = new EventBus();
        var observed = new List<GameEvent>();
        bus.SubscribeAll(_ => throw new Exception("global boom"));
        bus.SubscribeAll(observed.Add);

        bus.Publish(new TestEvent("g"));

        observed.Should().HaveCount(1);
    }

    [Fact]
    public void Unsubscribe_RemovesAsyncHandler()
    {
        var bus = new EventBus();
        var hits = 0;
        Func<TestEvent, Task> handler = async _ => { await Task.Yield(); Interlocked.Increment(ref hits); };
        bus.Subscribe(handler);
        bus.Unsubscribe(handler);

        bus.Publish(new TestEvent("u"));

        hits.Should().Be(0);
    }
}
