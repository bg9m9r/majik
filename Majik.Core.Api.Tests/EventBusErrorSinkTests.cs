using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Events;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Slice 4a Fix #1 — the per-match EventBus inside <see cref="GameFacade"/>
/// must route handler exceptions to <see cref="GameFacade.OnEventHandlerError"/>
/// instead of swallowing them silently. A throwing event subscriber is
/// dispatched through the bus's SubscribeAll bridge, so its exception is
/// caught by EventBus.SafeInvoke and must reach the wired sink.
/// </summary>
public class EventBusErrorSinkTests : IDisposable
{
    private readonly Action<Exception, GameEvent>? _previousSink;

    public EventBusErrorSinkTests()
    {
        // Capture and clear any ambient sink so these tests are hermetic and
        // can install their own (and so the DEBUG fail-fast default doesn't
        // crash the test host on the throwing-subscriber path).
        _previousSink = GameFacade.OnEventHandlerError;
    }

    public void Dispose() => GameFacade.OnEventHandlerError = _previousSink;

    [Fact]
    public async Task ThrowingSubscriber_RoutesToOnEventHandlerError_AndDoesNotPropagateToPublisher()
    {
        var captured = new List<Exception>();
        GameFacade.OnEventHandlerError = (ex, _) => captured.Add(ex);

        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());

        // A throwing wire subscriber is dispatched inside the facade's
        // SubscribeAll bridge handler, so its exception is caught by the
        // EventBus and must be routed to the wired sink — never swallowed
        // silently, and never propagated back to the engine publisher
        // (which would freeze the game).
        facade.Subscribe(_ => throw new InvalidOperationException("bad wire subscriber"));

        // StartAsync drives engine events; it must complete normally rather
        // than surfacing the subscriber's throw to the caller.
        Func<Task> act = () => facade.StartAsync();
        await act.Should().NotThrowAsync(
            "handler exceptions must never propagate to the publisher / freeze the engine");

        captured.Should().NotBeEmpty("a throwing event subscriber must surface via the wired sink, not vanish");
        captured.Should().Contain(e => e is InvalidOperationException && e.Message == "bad wire subscriber");
    }

    [Fact]
    public async Task NoThrow_LeavesSinkUntouched()
    {
        var captured = new List<Exception>();
        GameFacade.OnEventHandlerError = (ex, _) => captured.Add(ex);

        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        facade.Subscribe(_ => { /* well-behaved */ });

        await facade.StartAsync();

        captured.Should().BeEmpty("no handler threw, so the error sink must never fire");
    }
}
