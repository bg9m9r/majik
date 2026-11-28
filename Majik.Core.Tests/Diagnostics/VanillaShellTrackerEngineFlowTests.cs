using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Diagnostics;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Diagnostics;

/// <summary>
/// The tracker's logger is consumer code and runs off the engine flow. Its
/// publish on the engine's own bus is engine work: bus handlers are engine
/// handlers (the facade's own subscribers read live state), so they must run on
/// the engine flow like every other publish.
/// </summary>
public class VanillaShellTrackerEngineFlowTests
{
    [Fact]
    public void Notice_PublishesOnTheEngineFlow_AndLogsOffIt()
    {
        var bus = new EventBus();
        bool? handlerOnFlow = null, loggerOnFlow = null;
        var engine = new object();
        bus.Subscribe<UnimplementedCardEncounteredEvent>(_ => handlerOnFlow = ReferenceEquals(EngineFlow.Current, engine));
        var tracker = new VanillaShellTracker(bus, _ => loggerOnFlow = EngineFlow.Current != null);
        var card = new Creature("Unbuilt Thing", "{1}", 1, 1);
        card.MarkAsVanillaShell();

        var previous = EngineFlow.Current;
        EngineFlow.Current = engine;
        try { tracker.Notice(card, new Player("Alice", 20), "cast").Should().BeTrue(); }
        finally { EngineFlow.Current = previous; }

        handlerOnFlow.Should().BeTrue("the engine bus publish stays on the engine flow");
        loggerOnFlow.Should().BeFalse("the consumer logger runs off the engine flow");
    }
}