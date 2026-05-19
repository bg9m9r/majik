using FluentAssertions;
using Majik.Server.Hubs;
using Xunit;

namespace Majik.Server.Tests;

/// <summary>Locks the per-connection routing contract used by
/// GameHubBridge to push prompts only to the right player.</summary>
public class HubConnectionRegistryTests
{
    [Fact]
    public void Register_ThenConnectionsForPlayer_ReturnsRegisteredId()
    {
        var registry = new HubConnectionRegistry();
        var gameId = Guid.NewGuid();
        var alice = Guid.NewGuid();
        registry.Register(gameId, "conn-1", new[] { alice });

        registry.ConnectionsForPlayer(gameId, alice).Should().Equal("conn-1");
    }

    [Fact]
    public void ConnectionsForPlayer_NoMatch_ReturnsEmpty()
    {
        var registry = new HubConnectionRegistry();
        var gameId = Guid.NewGuid();
        registry.Register(gameId, "conn-1", new[] { Guid.NewGuid() });

        registry.ConnectionsForPlayer(gameId, Guid.NewGuid()).Should().BeEmpty();
    }

    [Fact]
    public void Unregister_DropsConnectionAcrossAllGames()
    {
        var registry = new HubConnectionRegistry();
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var alice = Guid.NewGuid();
        registry.Register(g1, "conn-1", new[] { alice });
        registry.Register(g2, "conn-1", new[] { alice });

        registry.Unregister("conn-1");

        registry.ConnectionsForPlayer(g1, alice).Should().BeEmpty();
        registry.ConnectionsForPlayer(g2, alice).Should().BeEmpty();
    }

    [Fact]
    public void UnregisterFromGame_OnlyAffectsThatGame()
    {
        var registry = new HubConnectionRegistry();
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var alice = Guid.NewGuid();
        registry.Register(g1, "conn-1", new[] { alice });
        registry.Register(g2, "conn-1", new[] { alice });

        registry.UnregisterFromGame(g1, "conn-1");

        registry.ConnectionsForPlayer(g1, alice).Should().BeEmpty();
        registry.ConnectionsForPlayer(g2, alice).Should().Equal("conn-1");
    }

    [Fact]
    public void Register_TwoConnectionsForSameSlot_BothReturned()
    {
        var registry = new HubConnectionRegistry();
        var gameId = Guid.NewGuid();
        var alice = Guid.NewGuid();
        registry.Register(gameId, "conn-1", new[] { alice });
        registry.Register(gameId, "conn-2", new[] { alice });

        registry.ConnectionsForPlayer(gameId, alice)
            .Should().BeEquivalentTo(new[] { "conn-1", "conn-2" });
    }
}
