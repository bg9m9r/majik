using System.Linq;
using FluentAssertions;
using Majik.Core.Game;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// Task 3.1 — <see cref="GameContext"/> exposes the live per-turn
/// <see cref="TurnState"/> and the <see cref="GameContext.Opponents"/> set so
/// resolution-time effects and context-aware activation gates can read them off
/// the context (instead of a captured build-time resolver / TurnState).
/// </summary>
public class GameContextTurnStateOpponentsTests
{
    private static GameContext Ctx(Player self, TurnState? ts, params Player[] all) =>
        new(
            self: self,
            allPlayers: all,
            activePlayer: self,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(),
            landPlayAvailable: true,
            turnState: ts);

    [Fact]
    public void ExistingCtor_LeavesTurnStateNull()
    {
        var alice = new Player("Alice", 20);
        var ctx = new GameContext(
            self: alice,
            allPlayers: new[] { alice },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack());

        ctx.TurnState.Should().BeNull();
    }

    [Fact]
    public void Exposes_SuppliedTurnState()
    {
        var alice = new Player("Alice", 20);
        var ts = new TurnState();
        ts.RecordCreatureDied(alice);
        ts.RecordCreatureDied(alice);

        var ctx = Ctx(alice, ts, alice);

        ctx.TurnState.Should().BeSameAs(ts);
        ctx.TurnState!.CreaturesDiedThisTurn.Should().Be(2);
    }

    [Fact]
    public void Opponents_ExcludesSelf()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var ctx = Ctx(alice, null, alice, bob);

        ctx.Opponents.Should().ContainSingle().Which.Should().BeSameAs(bob);
    }

    [Fact]
    public void Opponents_ExcludesLostPlayers()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var carol = new Player("Carol", 20);
        carol.HasLost = true;

        var ctx = Ctx(alice, null, alice, bob, carol);

        ctx.Opponents.Select(p => p.Name).Should().BeEquivalentTo(new[] { "Bob" });
    }
}
