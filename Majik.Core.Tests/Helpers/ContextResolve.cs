using Majik.Core.Abilities;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.Tests.Helpers;

/// <summary>
/// Shared test helper for the "each opponent" / "each player" resolver-null
/// bug-class fix: resolve a triggered / activated / loyalty ability through its
/// async <c>ResolveAsync</c> path with a live <see cref="GameContext"/>, so the
/// effect reads "each opponent" from <c>rc.Game.AllPlayers</c> exactly as it
/// does in a real match (instead of from a captured resolver). Mirrors the
/// per-test helpers in <c>KnightOfTheWhiteOrchidFactoryTests</c> /
/// <c>GrayMerchantOfAsphodelFactoryTests</c>.
/// </summary>
public static class ContextResolve
{
    /// <summary>
    /// Build a minimal live <see cref="GameContext"/> over <paramref name="players"/>
    /// with <paramref name="controller"/> as self / active player.
    /// </summary>
    public static GameContext Game(Player controller, params Player[] players) =>
        new(
            self: controller,
            allPlayers: players,
            activePlayer: controller,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new EventBus()));

    /// <summary>
    /// Resolve <paramref name="trigger"/> with a live game built from
    /// <paramref name="players"/> (the first of which is treated as the
    /// controller).
    /// </summary>
    public static void Resolve(
        TriggeredAbility trigger, Player controller, params Player[] players)
    {
        trigger.ResolveAsync(agent: null, game: Game(controller, players))
            .AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Resolve <paramref name="ability"/> (an activated ability) with a live
    /// game built from <paramref name="players"/>.
    /// </summary>
    public static void Resolve(
        ActivatedAbility ability, Player controller, params Player[] players)
    {
        ability.ResolveAsync(agent: null, game: Game(controller, players))
            .AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Pop the top stack object and resolve it through a live game built from
    /// <paramref name="players"/> (the resolver-null bug-class fix routes the
    /// each-opponent read off the resolution context the stack threads in).
    /// </summary>
    public static void ResolveStackTop(
        Majik.Core.Stack.Stack stack, Player controller, params Player[] players)
    {
        stack.Pop()!.ResolveAsync(agent: null, game: Game(controller, players))
            .AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Run a loyalty ability's effects through a live <see cref="ResolutionContext"/>
    /// (the loyalty dispatch path threads the game into <c>rc.Game</c> in prod).
    /// </summary>
    public static void Resolve(
        LoyaltyAbility ability, Player controller, params Player[] players)
    {
        var rc = ResolutionContext.For(
            controller, agent: null, game: Game(controller, players), chosenTargets: null);
        foreach (var e in ability.Effects)
        {
            e.ExecuteAsync(rc).AsTask().GetAwaiter().GetResult();
        }
    }
}
