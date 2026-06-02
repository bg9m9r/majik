using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.Rules;

/// <summary>
/// Which top-of-library cards a "play from the top of your library" grant
/// (CR 601.3e / CR 305.6) lets its controller play.
/// </summary>
public enum TopPlayFilter
{
    /// <summary>Lands only — Courser of Kruphix, Oracle of Mul Daya, Augur of
    /// Autumn's land clause (CR 305.6).</summary>
    Lands,

    /// <summary>Creature cards only — Augur of Autumn's Coven clause,
    /// Vivien (Champion of the Wilds)-style creature top-play.</summary>
    Creatures,

    /// <summary>Any card — Bolas's Citadel / Vizier-of-the-Menagerie-adjacent
    /// "play the top card" effects.</summary>
    Any,
}

/// <summary>
/// CR 601.3e / CR 305.6 / CR 715.4 — process-level, per-game registry for the
/// "you may play [filter] from the top of your library" continuous permission
/// (plus the informational "play with the top card revealed" rider).
///
/// <para>
/// A battlefield static (Courser of Kruphix, Augur of Autumn, Oracle of Mul
/// Daya, …) registers a grant keyed by its source token while it is on the
/// battlefield and removes it on leave (CR 603.6e — a static functions only
/// while its source is on the battlefield). Each grant carries the controller
/// it benefits and a <see cref="TopPlayFilter"/> describing which top-of-library
/// cards become legal play sources.
/// </para>
///
/// <para>
/// CR 601.3e: an effect may allow a player to play a card "as though it were in
/// their hand" — the card is still played from its current zone (the library),
/// it still consumes the normal land drop (CR 305.2) and respects any
/// additional-land static, and only the top card is ever eligible. The engine's
/// land-play path (<see cref="Majik.Core.Game.PriorityLoop"/> →
/// <see cref="Majik.Core.Services.ZoneService.MoveCardToAsync"/>) already plays
/// a land from whatever zone the card occupies, so this registry is the
/// permission/visibility surface the agent + validators consult to know the top
/// library card is a legal play source — and the bus-aware lifecycle that
/// registers/revokes it as the source enters/leaves.
/// </para>
///
/// <para>
/// Reveal-top (CR 715.4) is tracked by the presence of any grant whose source
/// also reveals the top card — Courser / Oracle / Augur all play with the top
/// card revealed. <see cref="IsTopRevealed"/> exposes that for the bot / UI
/// surface (the top card is public information).
/// </para>
///
/// Grants stack across multiple sources (a player with both Courser and Augur
/// has two land grants — idempotent for the same filter). A card is playable
/// from the top iff it is currently the top of the controller's library AND at
/// least one registered grant for that controller matches its type.
///
/// Tests that mutate the registry should call <see cref="Clear"/> in a
/// fixture / dispose path to avoid leakage across cases.
/// </summary>
public static class LibraryTopPlayPermissions
{
    /// <summary>Per-game store: the token-keyed grant list and its lock.</summary>
    public sealed class Store
    {
        // Each entry: (token, controller, filter, revealsTop).
        internal readonly List<(object Token, Player Controller, TopPlayFilter Filter, bool RevealsTop)> Grants = new();
        internal readonly object Gate = new();
    }

    private static readonly AmbientRegistryStore<Store> _ambient = new();

    private static Store Current => _ambient.Current;

    /// <summary>Install a fresh per-game store. See <see cref="GameRegistryScope"/>.</summary>
    public static IDisposable PushScope() => _ambient.Push(new Store());

    /// <summary>
    /// Register a "may play [<paramref name="filter"/>] from the top of your
    /// library" grant on <paramref name="controller"/>, keyed by
    /// <paramref name="token"/> (the source permanent). Idempotent for the same
    /// (token, controller, filter) — re-registering does not add a duplicate.
    /// When <paramref name="revealsTop"/> is true the source also plays with the
    /// top card revealed (CR 715.4).
    /// </summary>
    public static void AddGrant(
        object token, Player controller, TopPlayFilter filter, bool revealsTop = true)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(controller);
        var store = Current;
        lock (store.Gate)
        {
            foreach (var g in store.Grants)
            {
                if (ReferenceEquals(g.Token, token)
                    && ReferenceEquals(g.Controller, controller)
                    && g.Filter == filter)
                {
                    return;
                }
            }
            store.Grants.Add((token, controller, filter, revealsTop));
        }
    }

    /// <summary>
    /// Remove every grant registered under <paramref name="token"/>. Used when
    /// the source permanent leaves the battlefield. Idempotent.
    /// </summary>
    public static void RemoveGrant(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var store = Current;
        lock (store.Gate)
        {
            store.Grants.RemoveAll(g => ReferenceEquals(g.Token, token));
        }
    }

    /// <summary>
    /// True if <paramref name="controller"/> currently has at least one grant
    /// whose filter would let them play a card of the given type-set from the
    /// top of their library — independent of whether such a card is actually on
    /// top right now. Use <see cref="MayPlayTopCard"/> for the live, on-top
    /// check.
    /// </summary>
    public static bool HasGrant(Player controller, TopPlayFilter filter)
    {
        if (controller == null) return false;
        var store = Current;
        lock (store.Gate)
        {
            foreach (var g in store.Grants)
            {
                if (ReferenceEquals(g.Controller, controller) && Covers(g.Filter, filter))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// True if <paramref name="card"/> is currently the top card of
    /// <paramref name="controller"/>'s library AND a registered grant for that
    /// controller covers its card type — i.e. the controller may play it from
    /// the top this turn (CR 601.3e). Lands additionally still cost the land
    /// drop, enforced by <see cref="Majik.Core.Game.LandDropTracker"/> on the
    /// play path; this method only answers the zone-source-legality half.
    /// </summary>
    public static bool MayPlayTopCard(Player controller, ICard card)
    {
        if (controller == null || card == null) return false;
        if (!ReferenceEquals(TopOfLibrary(controller), card)) return false;

        var store = Current;
        lock (store.Gate)
        {
            foreach (var g in store.Grants)
            {
                if (!ReferenceEquals(g.Controller, controller)) continue;
                if (Matches(g.Filter, card)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// The top card of <paramref name="controller"/>'s library if they currently
    /// have an active "play lands from the top" grant AND that top card is a
    /// land — i.e. the land they may play from the top this turn. Returns null
    /// otherwise. Convenience for the agent's land-drop enumeration.
    /// </summary>
    public static ICard? PlayableLandFromTop(Player controller)
    {
        if (controller == null) return null;
        var top = TopOfLibrary(controller);
        if (top == null) return null;
        return MayPlayTopCard(controller, top) && top.HasType(CardType.Land) ? top : null;
    }

    /// <summary>
    /// True if any active grant for <paramref name="controller"/> reveals the
    /// top card of their library (CR 715.4). When true the top card is public
    /// information (bot / UI may surface it).
    /// </summary>
    public static bool IsTopRevealed(Player controller)
    {
        if (controller == null) return false;
        var store = Current;
        lock (store.Gate)
        {
            foreach (var g in store.Grants)
            {
                if (ReferenceEquals(g.Controller, controller) && g.RevealsTop) return true;
            }
            return false;
        }
    }

    /// <summary>Reset the active store. Test-only.</summary>
    public static void Clear()
    {
        var store = Current;
        lock (store.Gate) store.Grants.Clear();
    }

    private static ICard? TopOfLibrary(Player controller) =>
        controller.Zones.Library.GetCards().FirstOrDefault();

    // A grant with `granted` filter covers a `wanted` capability when the
    // granted filter is at least as permissive: Any covers everything; a
    // specific filter covers only its own kind.
    private static bool Covers(TopPlayFilter granted, TopPlayFilter wanted) =>
        granted == TopPlayFilter.Any || granted == wanted;

    private static bool Matches(TopPlayFilter filter, ICard card) => filter switch
    {
        TopPlayFilter.Lands => card.HasType(CardType.Land),
        TopPlayFilter.Creatures => card.HasType(CardType.Creature),
        TopPlayFilter.Any => true,
        _ => false,
    };
}
