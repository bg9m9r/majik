using Majik.Core.Abilities;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Shared helper for the "each opponent" / "each player" resolver-null bug
/// class. Reads the relevant players from the LIVE resolution context
/// (<see cref="ResolutionContext.Game"/> → <c>GameContext.AllPlayers</c>) at
/// RESOLUTION instead of from a <see cref="Func{T}"/> resolver captured at
/// factory-build time.
///
/// <para>
/// The production routed build
/// (<c>GameFacade.BuildDeckCard → NamedCardFactory.Create(name, owner)</c>)
/// dispatches the single-arg <c>Create(Player)</c> overload, leaving any
/// captured <c>opponentResolver</c> / <c>opponentsResolver</c> null — so an
/// each-opponent effect that read the resolver was INERT in real games (only
/// resolver-injecting factory-direct tests ever saw it run). The triggered /
/// activated ability itself IS live on the routed build (auto-registered via
/// <c>TriggerManager.BindCard</c> by zone), so the only fix needed is to make
/// the effect body read the live game off the <see cref="ResolutionContext"/>
/// the ability already threads through <c>ResolveAsync</c>. Mirrors the
/// established fix on Stormbreath (#2540), Yawgmoth / Priest (#2543) and
/// Grist / Soul-Guide Lantern / Knight (#2549).
/// </para>
/// </summary>
public static class ContextOpponents
{
    /// <summary>
    /// CR 102.1 / 102.4 — every opponent of <paramref name="controller"/> that
    /// is still in the game, read from the live resolution context. Returns an
    /// empty sequence when no live game context is available (shape-only paths),
    /// so the each-opponent clause is a safe no-op rather than throwing.
    /// </summary>
    public static IEnumerable<Player> Of(ResolutionContext ctx, Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        var players = ctx?.Game?.AllPlayers;
        if (players == null) yield break;
        foreach (var p in players)
        {
            if (p == null) continue;
            // CR 102.1 — a player is never their own opponent.
            if (ReferenceEquals(p, controller)) continue;
            // CR 800.4a — a player who has left the game is no longer an opponent.
            if (p.HasLost) continue;
            yield return p;
        }
    }
}
