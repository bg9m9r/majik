using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// CR 708.9 — a face-down permanent leaving the battlefield is revealed to
/// everyone: the move names the card it was, and the new zone shows it face up.
/// </summary>
public class FaceDownLeavesBattlefieldPayloadTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ADeadManifest_IsRevealed_InTheMoveAndTheGraveyard()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        var wrapper = ManifestEffect.ManifestCard(_alice, bear, zones);
        CardMovedEvent? moved = null;
        bus.Subscribe<CardMovedEvent>(e => moved = e);

        zones.MoveCard(wrapper, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        EventPayloadBuilder.RequiresPerViewerMasking(moved!).Should().BeFalse("revealed to all");
        var payload = EventPayloadBuilder.Build(moved!, _bob);
        payload.GetProperty("cardId").GetGuid().Should().Be(wrapper.InstanceId, "the id clients know on the battlefield");
        payload.GetProperty("revealedCardId").GetGuid().Should().Be(bear.InstanceId, "the card now in the graveyard");
        payload.GetProperty("cardName").GetString().Should().Be("Grizzly Bears");
        payload.TryGetProperty("isFaceDown", out _).Should().BeFalse();

        var grave = StateSnapshotter.Snapshot(Guid.Empty, 1, null, _alice, new[] { _alice, _bob }, new Majik.Core.Stack.Stack(), _bob)
            .Players[0].Graveyard.Cards.Should().ContainSingle().Subject;
        grave.Name.Should().Be("Grizzly Bears");
        grave.IsFaceDown.Should().BeFalse();
        grave.InstanceId.Should().Be(bear.InstanceId);
    }
}
