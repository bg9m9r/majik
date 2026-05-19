using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>Locks the EventPayloadBuilder mapping. Wire-format payload
/// shapes are part of the API contract — changes here are visible to
/// the frontend.</summary>
public class EventPayloadTests
{
    [Fact]
    public void CardMovedEvent_PayloadContainsCardIdAndZones()
    {
        var card = new Card("Lightning Bolt", "R");
        var e = new CardMovedEvent(card, ZoneType.Hand, ZoneType.Stack);

        var payload = EventPayloadBuilder.Build(e);

        payload.GetProperty("cardId").GetGuid().Should().Be(card.InstanceId);
        payload.GetProperty("cardName").GetString().Should().Be("Lightning Bolt");
        payload.GetProperty("from").GetString().Should().Be("Hand");
        payload.GetProperty("to").GetString().Should().Be("Stack");
    }

    [Fact]
    public void LifeChangedEvent_PayloadCarriesPreviousAndCurrent()
    {
        var alice = new Player("Alice");
        var e = new LifeChangedEvent(alice, 20, 17);

        var payload = EventPayloadBuilder.Build(e);

        payload.GetProperty("playerId").GetGuid().Should().Be(alice.Id);
        payload.GetProperty("previous").GetInt32().Should().Be(20);
        payload.GetProperty("current").GetInt32().Should().Be(17);
    }

    [Fact]
    public void UnknownEvent_FallsBackToEmptyPayload()
    {
        // GameStartedEvent is the only known no-fields event but still
        // exercises the fallback path.
        var payload = EventPayloadBuilder.Build(new GameStartedEvent());

        payload.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
        payload.EnumerateObject().Should().BeEmpty();
    }
}
