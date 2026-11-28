using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Game;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// The engine's LoopSafetyNet publishes a LoopSafetyNetTrippedEvent when it
/// ends a runaway game as a draw (CR 104.4b). GameFacade reports it through the
/// contained-fault channel so hosts and test harnesses see it.
/// </summary>
public class LoopSafetyNetReportTests
{
    [Fact]
    [ExpectsContainedFault]
    public void ATrip_IsReportedAsAContainedFault_WithItsMessage()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var reported = new List<(Exception Ex, string Context)>();
        void OnFault(Exception ex, string ctx) { lock (reported) reported.Add((ex, ctx)); }
        GameFacade.ContainedFaultReported += OnFault;
        try
        {
            const string message = "Loop safety net tripped on turn 3 (test). The game is ended as a draw (CR 104.4b).";
            facade.EventBus.Publish(new LoopSafetyNetTrippedEvent(message, 10_001, 2));

            var mine = reported.Where(r => r.Context == message).ToList();
            mine.Should().ContainSingle();
            mine[0].Ex.Should().BeOfType<LoopSafetyNetException>().Which.Message.Should().Be(message);
        }
        finally
        {
            GameFacade.ContainedFaultReported -= OnFault;
        }
    }

    [Fact]
    public void TheTrippedEvent_CarriesItsMessageAndCounts_OnTheWire()
    {
        var json = EventPayloadBuilder.Build(new LoopSafetyNetTrippedEvent("why", 10_001, 2));
        json.GetProperty("message").GetString().Should().Be("why");
        json.GetProperty("actionsThisStep").GetInt32().Should().Be(10_001);
        json.GetProperty("stackDepth").GetInt32().Should().Be(2);
    }
}
