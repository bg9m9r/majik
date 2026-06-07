using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Simulation;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Simulation;

public sealed class SandboxGameTests
{
    /// <summary>
    /// Smoke test: SandboxGame clones live state, builds the engine subsystem
    /// stack, and runs a 2-turn game without throwing. The originals must be
    /// entirely untouched after the run (no draws, no life change) because the
    /// sandbox operates on clones, not the originals.
    /// </summary>
    [Fact]
    public async Task Sandbox_RunsMinimalGame_WithoutThrowing_AndLeavesOriginalsUntouched()
    {
        // Minimal, vanilla board: two players, libraries of basic lands only,
        // empty battlefields. No static abilities → avoids continuous-effects edge.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        SeedLibrary(alice, 20);
        SeedLibrary(bob, 20);

        var aliceLifeBefore = alice.LifeTotal;
        var aliceLibBefore = alice.Zones.Library.GetCards().Count();

        var sandbox = SandboxGame.From(
            new[] { alice, bob },
            rng: new GameRandom(seed: 1),
            agentFactory: _ => new DeterministicBotAgent());

        await sandbox.Driver.RunGameAsync(maxTurns: 2, startingPlayerIndex: 0, CancellationToken.None);

        // Sandbox ran on CLONES — originals must be untouched (no draws, no life change).
        alice.LifeTotal.Should().Be(aliceLifeBefore);
        alice.Zones.Library.GetCards().Count().Should().Be(aliceLibBefore);
        sandbox.HasIoBridge.Should().BeFalse();
    }

    private static void SeedLibrary(Player player, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var land = new Land("Forest");
            land.ChangeOwner(player);
            player.Zones.Library.AddCard(land);
            land.SetZone(ZoneType.Library);
        }
    }
}
