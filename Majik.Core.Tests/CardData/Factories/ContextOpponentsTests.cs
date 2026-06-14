using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Game;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// <see cref="ContextOpponents.Of"/> reads the "each opponent" set off the live
/// resolution context (CR 102.1 / 102.4 / 800.4a). It is the prod each-opponent
/// path for Gray Merchant / Sheoldred / Hired Claw etc. The common shape — the
/// resolving controller IS the context's <see cref="GameContext.Self"/> — must
/// reuse the memoized <see cref="GameContext.Opponents"/> list (added in #2687)
/// rather than spinning up a fresh iterator + filter on every resolution.
/// </summary>
public class ContextOpponentsTests
{
    private static GameContext Ctx(Player self, params Player[] all) =>
        new(
            self: self,
            allPlayers: all,
            activePlayer: self,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack());

    private static ResolutionContext Rc(GameContext game, Player controller) =>
        ResolutionContext.For(controller, agent: null, game: game, chosenTargets: null);

    [Fact]
    public void Of_ExcludesControllerAndLostPlayers()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var carol = new Player("Carol", 20) { HasLost = true };
        var ctx = Ctx(alice, alice, bob, carol);

        ContextOpponents.Of(Rc(ctx, alice), alice)
            .Select(p => p.Name)
            .Should().BeEquivalentTo(new[] { "Bob" });
    }

    [Fact]
    public void Of_NoGameContext_ReturnsEmpty()
    {
        var alice = new Player("Alice", 20);
        ContextOpponents.Of(ResolutionContext.Legacy, alice).Should().BeEmpty();
    }

    [Fact]
    public void Of_OtherController_StillFiltersAgainstThatController()
    {
        // controller != ctx.Self: opponents are everyone-but-bob, computed live.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var ctx = Ctx(alice, alice, bob);

        ContextOpponents.Of(Rc(ctx, bob), bob)
            .Select(p => p.Name)
            .Should().BeEquivalentTo(new[] { "Alice" });
    }

    [Fact]
    public void Of_ControllerIsSelf_ReusesMemoizedOpponentsList_NoPerCallAlloc()
    {
        // Perf: the each-opponent resolution path (Gray Merchant, Sheoldred,
        // Hired Claw, …) should reuse the cached GameContext.Opponents list when
        // the controller IS the context's Self — not materialize a fresh list per
        // call. Reference-equal to the memoized accessor result proves the reuse.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var ctx = Ctx(alice, alice, bob);

        var cached = ctx.Opponents;
        var fromHelper = ContextOpponents.Of(Rc(ctx, alice), alice);

        fromHelper.Should().BeSameAs(cached);
    }
}
