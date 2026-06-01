using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.Rules;

/// <summary>
/// CR 702.11 / CR 113 — process-level registry for <em>player-targeted</em>
/// static-ability grants imposed by other game objects (e.g. Leyline of
/// Sanctity's "You have hexproof"; future True Believer / Mana Maze rider
/// support).
///
/// Player keywords are tracked per (player, source-token) so multiple
/// sources can stack without trampling each other; a player is treated as
/// having the keyword iff at least one entry targeting them is currently
/// registered. The keyword is binary — two Leylines registering hexproof
/// against the same player is idempotent (CR 702.11b — having a keyword
/// twice has no extra effect).
///
/// The registry is a singleton-style static service keyed by reference
/// equality on the source token. <see cref="PlayerHexproofEffect"/> is
/// the canonical caller — it registers/unregisters as its source
/// permanent enters/leaves the battlefield via
/// <see cref="Majik.Core.Events.CardMovedEvent"/>.
///
/// <see cref="ActionValidator"/> consults
/// <see cref="HasHexproof(Player)"/> when validating a spell or activated-
/// ability cast that names a player target: when the target has hexproof
/// and the controller differs from the target, the cast is rejected with
/// <see cref="RuleViolation"/> 702.11. <see cref="Player.HasHexproof"/>
/// proxies the same check for any non-validator caller.
///
/// Tests that mutate the registry should call <see cref="Clear"/> in a
/// fixture/dispose path to avoid leakage across cases.
/// </summary>
public static class PlayerStaticAbilities
{
    /// <summary>Per-game store: the token/player hexproof grant list and lock.</summary>
    public sealed class Store
    {
        // Each entry: (token, player). A player has hexproof while at least
        // one entry targeting them exists.
        internal readonly List<(object Token, Player Player)> Hexproof = new();
        internal readonly object Gate = new();
    }

    private static readonly AmbientRegistryStore<Store> _ambient = new();

    private static Store Current => _ambient.Current;

    /// <summary>Install a fresh per-game store. See <see cref="GameRegistryScope"/>.</summary>
    public static IDisposable PushScope() => _ambient.Push(new Store());

    /// <summary>
    /// Register a hexproof grant on <paramref name="player"/>, keyed by
    /// <paramref name="token"/>. Idempotent for the same (token, player)
    /// pair — re-registering does not add a second entry. Multiple
    /// distinct tokens granting hexproof against the same player stack
    /// without trampling; <see cref="HasHexproof"/> returns true while
    /// any entry survives.
    /// </summary>
    public static void AddHexproof(object token, Player player)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(player);
        var store = Current;
        lock (store.Gate)
        {
            foreach (var entry in store.Hexproof)
            {
                if (ReferenceEquals(entry.Token, token)
                    && ReferenceEquals(entry.Player, player))
                {
                    return;
                }
            }
            store.Hexproof.Add((token, player));
        }
    }

    /// <summary>
    /// Remove every hexproof grant registered under
    /// <paramref name="token"/> (across all players). Used when the
    /// source permanent leaves the battlefield.
    /// </summary>
    public static void RemoveHexproof(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var store = Current;
        lock (store.Gate)
        {
            store.Hexproof.RemoveAll(e => ReferenceEquals(e.Token, token));
        }
    }

    /// <summary>
    /// True if at least one registered grant currently gives
    /// <paramref name="player"/> player-hexproof (CR 702.11).
    /// </summary>
    public static bool HasHexproof(Player player)
    {
        if (player == null) return false;
        var store = Current;
        lock (store.Gate)
        {
            foreach (var entry in store.Hexproof)
            {
                if (ReferenceEquals(entry.Player, player)) return true;
            }
            return false;
        }
    }

    /// <summary>Reset the active store. Test-only.</summary>
    public static void Clear()
    {
        var store = Current;
        lock (store.Gate) store.Hexproof.Clear();
    }
}
