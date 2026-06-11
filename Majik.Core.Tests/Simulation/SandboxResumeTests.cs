using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Simulation;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Simulation;

public sealed class SandboxResumeTests
{
    /// <summary>
    /// Verifies that SandboxGame.ResumeAsync:
    ///   1. Does NOT reshuffle libraries (library order preserved on the ORIGINAL — sandboxing is on clones).
    ///   2. Does NOT mutate the original player objects (life, library size untouched).
    ///   3. Completes without throwing (partial-turn resume + subsequent turns).
    /// </summary>
    [Fact]
    public async Task Sandbox_Resume_FromPostCombatMain_NoReshuffle_OriginalsUntouched()
    {
        var (alice, bob) = SimResumeBoards.VanillaMidGame();

        // Capture original library instance-id order BEFORE building the sandbox.
        var aliceLibBefore = alice.Zones.GetZone(ZoneType.Library).GetCards()
            .Select(c => c.InstanceId).ToList();
        var aliceLifeBefore = alice.LifeTotal;

        var sandbox = SandboxGame.From(
            new[] { alice, bob },
            rng: new GameRandom(1),
            agentFactory: _ => new DeterministicBotAgent());

        // ResumeAsync is the method under test — won't compile until implemented.
        await sandbox.ResumeAsync(
            resumePhase: PhaseStateType.PostCombatMain,
            activePlayer: sandbox.State.PlayerFor(alice),
            turnNumber: 4,
            maxTurns: 7,
            ct: default);

        // sandbox ran on CLONES; originals must be untouched.
        // Library order MUST be identical — no reshuffle happened.
        alice.Zones.GetZone(ZoneType.Library).GetCards()
            .Select(c => c.InstanceId)
            .Should().Equal(aliceLibBefore,
                because: "ResumeAsync must not reshuffle libraries");

        alice.LifeTotal.Should().Be(aliceLifeBefore,
            because: "sandbox mutations never reach the originals");
    }
}

/// <summary>
/// Shared board factory for SandboxResumeTests. Builds a minimal mid-game
/// position: two players, a couple of vanilla creatures + a land on each
/// battlefield, ~15 Forests in each library.
/// </summary>
internal static class SimResumeBoards
{
    public static (Player Alice, Player Bob) VanillaMidGame()
    {
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   20);

        // Put a couple of vanilla creatures on each battlefield.
        AddCreatureToBoard(alice, "Grizzly Bears",     "1G", 2, 2);
        AddCreatureToBoard(alice, "Llanowar Elves",    "G",  1, 1);
        AddCreatureToBoard(bob,   "Eager First-Year",  "1W", 1, 2);
        AddCreatureToBoard(bob,   "Elvish Mystic",     "G",  1, 1);

        // A land on each battlefield.
        AddLandToBoard(alice, "Forest");
        AddLandToBoard(bob,   "Plains");

        // ~15 Forests in each library (gives the engine cards to draw).
        for (var i = 0; i < 15; i++)
        {
            AddLandToLibrary(alice, "Forest");
            AddLandToLibrary(bob,   "Forest");
        }

        return (alice, bob);
    }

    private static void AddCreatureToBoard(Player owner, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.ChangeOwner(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
    }

    private static void AddLandToBoard(Player owner, string name)
    {
        var land = new Land(name);
        land.ChangeOwner(owner);
        owner.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
    }

    private static void AddLandToLibrary(Player owner, string name)
    {
        var land = new Land(name);
        land.ChangeOwner(owner);
        owner.Zones.Library.AddCard(land);
        land.SetZone(ZoneType.Library);
    }
}
