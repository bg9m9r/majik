using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

public class HiddenInfoTests
{
    private readonly EventBus _bus = new();

    [Fact]
    public void SpectatorView_RevealsAllHands()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        SeedHand(alice, "AliceSecret");
        SeedHand(bob, "BobSecret");

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.Main, alice,
            new[] { alice, bob }, new Majik.Core.Stack.Stack(_bus),
            viewer: null);

        dto.Players.Single(p => p.Name == "Alice").Hand.Cards[0].Name.Should().Be("AliceSecret");
        dto.Players.Single(p => p.Name == "Bob").Hand.Cards[0].Name.Should().Be("BobSecret");
    }

    [Fact]
    public void AliceView_HidesBobsHand_RevealsOwn()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        SeedHand(alice, "AliceSecret");
        SeedHand(bob, "BobSecret");

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.Main, alice,
            new[] { alice, bob }, new Majik.Core.Stack.Stack(_bus),
            viewer: alice);

        dto.Players.Single(p => p.Name == "Alice").Hand.Cards[0].Name.Should().Be("AliceSecret");
        dto.Players.Single(p => p.Name == "Bob").Hand.Cards[0].Name.Should().Be("(hidden)");
    }

    [Fact]
    public void Library_AlwaysHidden_EvenToOwner()
    {
        var alice = new Player("Alice", 20);
        var c = new Card("LibSecret", "");
        c.Owner = alice; c.Zone = ZoneType.Library;
        alice.Zones.Library.AddCard(c);

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.Main, alice,
            new[] { alice }, new Majik.Core.Stack.Stack(_bus),
            viewer: alice);

        dto.Players[0].Library.Cards.Should().ContainSingle()
            .Which.Name.Should().Be("(hidden)");
    }

    [Fact]
    public void Battlefield_PublicEvenWithViewer()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = bob, Controller = bob };
        bear.Zone = ZoneType.Battlefield;
        bob.Zones.Battlefield.AddCard(bear);

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.Main, alice,
            new[] { alice, bob }, new Majik.Core.Stack.Stack(_bus),
            viewer: alice);

        dto.Players.Single(p => p.Name == "Bob").Battlefield.Cards[0].Name.Should().Be("Bear");
    }

    private static void SeedHand(Player player, string name)
    {
        var c = new Card(name, "");
        c.Owner = player; c.Zone = ZoneType.Hand;
        player.Zones.Hand.AddCard(c);
    }
}
