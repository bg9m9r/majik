using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// CR 406.3 — a card exiled face down is shown to everyone who may not look at
/// it the way a face-down permanent is (CR 708.2): <c>isFaceDown</c>, no name,
/// no characteristics; the real card only for the players the effect allows
/// (and the all-revealed spectator/debug view).
/// </summary>
public class FaceDownExileMaskingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public FaceDownExileMaskingTests() => _zones = new ZoneService(_bus);

    private Creature InLibrary()
    {
        var c = new Creature("Goblin Bear", "{2}{R}", 2, 2);
        c.SetOwner(_alice);
        c.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(c);
        return c;
    }

    private static Majik.Core.Api.Dtos.CardSnapshotDto ExiledAs(Player owner, Player? viewer) =>
        StateSnapshotter.Snapshot(Guid.Empty, 1, null, owner, new[] { owner }, new Majik.Core.Stack.Stack(), viewer)
            .Players.Single().Exile.Cards.Single();

    [Fact]
    public void Snapshot_MasksAFaceDownExiledCard_ForPlayersWhoMayNotLook()
    {
        var bear = InLibrary();
        _zones.ExileFaceDown(bear, ZoneType.Library, new[] { _alice }, _alice);

        var bobSees = ExiledAs(_alice, _bob);
        bobSees.IsFaceDown.Should().BeTrue();
        bobSees.Name.Should().Be(StateSnapshotter.FaceDownExileName);
        bobSees.InstanceId.Should().Be(bear.InstanceId);
        bobSees.ManaCost.Should().BeEmpty();
        bobSees.Types.Should().BeEmpty();
        bobSees.Power.Should().BeNull();
        bobSees.Abilities.Should().BeEmpty();
        bobSees.FaceDownCard.Should().BeNull();

        var aliceSees = ExiledAs(_alice, _alice);
        aliceSees.IsFaceDown.Should().BeTrue();
        aliceSees.Name.Should().Be("Goblin Bear");
        aliceSees.FaceDownCard!.Name.Should().Be("Goblin Bear");

        ExiledAs(_alice, null).Name.Should().Be("Goblin Bear", "the all-revealed view");
    }

    [Fact]
    public void Snapshot_ANobodyMayLookCard_IsMaskedForItsOwnerToo()
    {
        var bear = InLibrary();
        _zones.ExileFaceDown(bear, ZoneType.Library, Array.Empty<Player>(), _alice); // Bomat Courier

        ExiledAs(_alice, _alice).Name.Should().Be(StateSnapshotter.FaceDownExileName);
    }

    [Fact]
    public void CardMoved_IntoFaceDownExile_IsPerViewer_AndMaskedForNonLookers()
    {
        var bear = InLibrary();
        CardMovedEvent? moved = null;
        _bus.Subscribe<CardMovedEvent>(e => moved = e);
        _zones.ExileFaceDown(bear, ZoneType.Library, new[] { _alice }, _alice);

        EventPayloadBuilder.RequiresPerViewerMasking(moved!).Should().BeTrue();
        var bob = EventPayloadBuilder.Build(moved!, _bob);
        bob.GetProperty("cardName").GetString().Should().Be(StateSnapshotter.FaceDownExileName);
        bob.GetProperty("isFaceDown").GetBoolean().Should().BeTrue();
        bob.GetProperty("cardId").GetGuid().Should().Be(bear.InstanceId);
        bob.TryGetProperty("manaCost", out _).Should().BeFalse();
        bob.TryGetProperty("power", out _).Should().BeFalse();

        var alice = EventPayloadBuilder.Build(moved!, _alice);
        alice.GetProperty("cardName").GetString().Should().Be("Goblin Bear");
        alice.GetProperty("isFaceDown").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void CardMoved_FromFaceDownExileToAHand_StaysHiddenFromTheOpponent()
    {
        // Bomat Courier: "Put all cards exiled with this creature into their
        // owners' hands": Bob never saw them, and a hand is hidden (CR 706).
        var bear = InLibrary();
        _zones.ExileFaceDown(bear, ZoneType.Library, Array.Empty<Player>(), _alice);
        CardMovedEvent? moved = null;
        _bus.Subscribe<CardMovedEvent>(e => moved = e);

        _zones.MoveCard(bear, ZoneType.Exile, ZoneType.Hand, _alice);

        EventPayloadBuilder.RequiresPerViewerMasking(moved!).Should().BeTrue();
        var bob = EventPayloadBuilder.Build(moved!, _bob);
        bob.GetProperty("hidden").GetBoolean().Should().BeTrue();
        bob.TryGetProperty("cardName", out _).Should().BeFalse();
        EventPayloadBuilder.Build(moved!, _alice).GetProperty("cardName").GetString().Should().Be("Goblin Bear");
    }

    [Fact]
    public void AFaceUpExile_IsUnchanged()
    {
        var bear = InLibrary();
        CardMovedEvent? moved = null;
        _bus.Subscribe<CardMovedEvent>(e => moved = e);
        _zones.MoveCard(bear, ZoneType.Library, ZoneType.Exile, _alice);

        EventPayloadBuilder.RequiresPerViewerMasking(moved!).Should().BeFalse();
        EventPayloadBuilder.Build(moved!, _bob).GetProperty("cardName").GetString().Should().Be("Goblin Bear");
        ExiledAs(_alice, _bob).IsFaceDown.Should().BeFalse();
    }
}
