using FluentAssertions;
using Majik.Core.Game;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// Tests for <see cref="GamePlayersRegistry"/> — the ambient per-game player
/// set that lets mana-ability riders (Grove of the Burnwillows' "Each opponent
/// gains 1 life") read "each opponent" with no ResolutionContext (CR 605.3).
/// </summary>
public class GamePlayersRegistryTests
{
    [Fact]
    public void OpponentsOf_ExcludesController_CR_102_4()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var carol = new Player("Carol", 20);

        using var _ = GamePlayersRegistry.PushScope();
        GamePlayersRegistry.Set(new[] { alice, bob, carol });

        GamePlayersRegistry.OpponentsOf(alice).Should().BeEquivalentTo(new[] { bob, carol });
    }

    [Fact]
    public void OpponentsOf_ExcludesPlayersWhoLeftTheGame_CR_800_4a()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        bob.MarkLost();

        using var _ = GamePlayersRegistry.PushScope();
        GamePlayersRegistry.Set(new[] { alice, bob });

        GamePlayersRegistry.OpponentsOf(alice).Should().BeEmpty();
    }

    [Fact]
    public void OpponentsOf_NoPlayersInstalled_IsEmpty()
    {
        var alice = new Player("Alice", 20);

        using var _ = GamePlayersRegistry.PushScope();
        // No Set() call.

        GamePlayersRegistry.OpponentsOf(alice).Should().BeEmpty();
        GamePlayersRegistry.AllPlayers.Should().BeEmpty();
    }

    [Fact]
    public void Scopes_AreIsolated()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        using (var _ = GamePlayersRegistry.PushScope())
        {
            GamePlayersRegistry.Set(new[] { alice, bob });
            GamePlayersRegistry.AllPlayers.Should().HaveCount(2);
        }

        // Outside the disposed scope the entry is gone (process fallback store,
        // not seeded here).
        GamePlayersRegistry.AllPlayers.Should().NotContain(alice);
    }
}
