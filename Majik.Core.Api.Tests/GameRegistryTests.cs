using FluentAssertions;
using Majik.Core.Api;
using Xunit;

namespace Majik.Core.Api.Tests;

public class GameRegistryTests
{
    [Fact]
    public void Create_StoresFacade_RetrievableById()
    {
        var registry = new GameRegistry();

        var facade = registry.Create("Alice", "Bob");

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
        var facade = registry.Create("Alice", "Bob");

        registry.Remove(facade.GameId).Should().BeTrue();

        registry.Get(facade.GameId).Should().BeNull();
    }

    [Fact]
    public void Concurrent_Create_ProducesUniqueIds()
    {
        var registry = new GameRegistry();

        var games = Enumerable.Range(0, 100)
            .AsParallel()
            .Select(_ => registry.Create("Alice", "Bob"))
            .ToList();

        games.Select(g => g.GameId).Distinct().Should().HaveCount(100);
    }
}
