using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Api.Tests;

public class GameRegistryTests
{
    // ── Per-match registry leak (the leak fix) ──────────────────────────

    [Fact]
    public void Remove_DisposesFacade_PruningAgentRegistry()
    {
        var registry = new GameRegistry();
        var facade = registry.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());

        // GameFacade's ctor registers both seats' agents in the process-wide
        // fallback AgentRegistry store (used by direct-effect resolution before
        // a run is started). Pre-fix GameRegistry.Remove only did TryRemove and
        // never disposed the facade, so these entries leaked forever.
        AgentRegistry.Get(facade.Alice).Should().NotBeNull();
        AgentRegistry.Get(facade.Bob).Should().NotBeNull();

        registry.Remove(facade.GameId).Should().BeTrue();

        // Remove now disposes the facade, which prunes both seats.
        AgentRegistry.Get(facade.Alice).Should().BeNull();
        AgentRegistry.Get(facade.Bob).Should().BeNull();
    }

    [Fact]
    public void Remove_UnknownId_ReturnsFalse()
    {
        var registry = new GameRegistry();
        registry.Remove(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void Create_StoresFacade_RetrievableById()
    {
        var registry = new GameRegistry();

        var facade = registry.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());

        registry.Get(facade.GameId).Should().BeSameAs(facade);
    }

    [Fact]
    public void Get_UnknownId_ReturnsNull()
    {
        var registry = new GameRegistry();

        registry.Get(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void Remove_DeletesEntry()
    {
        var registry = new GameRegistry();
        var facade = registry.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());

        registry.Remove(facade.GameId).Should().BeTrue();

        registry.Get(facade.GameId).Should().BeNull();
    }

    [Fact]
    public void Concurrent_Create_ProducesUniqueIds()
    {
        var registry = new GameRegistry();

        var games = Enumerable.Range(0, 100)
            .AsParallel()
            .Select(_ => registry.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>()))
            .ToList();

        games.Select(g => g.GameId).Distinct().Should().HaveCount(100);
    }
}
