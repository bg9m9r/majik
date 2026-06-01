using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.Rules;

/// <summary>
/// CR 117.5 / 614.12 — process-level registry for "skip your draw step"
/// effects (Necropotence, Yawgmoth's Bargain, Dauthi Slayer, etc.). The
/// turn driver consults <see cref="ShouldSkipDraw(Player)"/> before
/// drawing the active player's normal draw-step card; when any
/// registered predicate matches the active player, the draw is skipped.
///
/// Modelled on <see cref="FlashGrantRegistry"/> — grants are token-keyed
/// so multiple sources can stack without trampling each other, and any
/// single matching predicate is enough to suppress the draw. Registered
/// via a card's static-ability lifecycle while its source is on the
/// battlefield; callers Remove on the source leaving the battlefield.
///
/// <para>
/// The backing state is scoped per-game via an
/// <see cref="AmbientRegistryStore{TStore}"/> /
/// <see cref="GameRegistryScope.PushForGame"/> (same pattern as
/// <see cref="CastingRestrictions"/>): concurrent matches see independent
/// state, and direct-construction tests resolve a process-wide fallback so
/// the static call sites keep working unchanged.
/// </para>
///
/// Tests that mutate the registry should call <see cref="Clear"/> in a
/// fixture/dispose path to avoid leakage across cases.
/// </summary>
public static class SkipDrawRegistry
{
    /// <summary>Per-game store: the token-keyed grant list and its lock.</summary>
    public sealed class Store
    {
        internal readonly List<(object Token, Func<Player, bool> Predicate)> Grants = new();
        internal readonly object Gate = new();
    }

    private static readonly AmbientRegistryStore<Store> _ambient = new();

    private static Store Current => _ambient.Current;

    /// <summary>Install a fresh per-game store. See <see cref="GameRegistryScope"/>.</summary>
    public static IDisposable PushScope() => _ambient.Push(new Store());

    /// <summary>
    /// Register a skip-draw predicate keyed by <paramref name="token"/>.
    /// Idempotent for the same token — re-registering replaces the prior
    /// predicate so the latest one wins.
    /// </summary>
    public static void AddSkip(object token, Func<Player, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(predicate);
        var store = Current;
        lock (store.Gate)
        {
            for (int i = 0; i < store.Grants.Count; i++)
            {
                if (ReferenceEquals(store.Grants[i].Token, token))
                {
                    store.Grants[i] = (token, predicate);
                    return;
                }
            }
            store.Grants.Add((token, predicate));
        }
    }

    /// <summary>
    /// Remove the skip-draw predicate registered under
    /// <paramref name="token"/>. Used when a source permanent leaves the
    /// battlefield. Idempotent (no-op if the token isn't registered).
    /// </summary>
    public static void RemoveSkip(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var store = Current;
        lock (store.Gate)
        {
            store.Grants.RemoveAll(g => ReferenceEquals(g.Token, token));
        }
    }

    /// <summary>
    /// True if any registered predicate matches the given player —
    /// i.e. that player's draw step should be skipped this turn. Returns
    /// false for null input or when no grant matches.
    /// </summary>
    public static bool ShouldSkipDraw(Player player)
    {
        if (player == null) return false;
        var store = Current;
        lock (store.Gate)
        {
            foreach (var (_, predicate) in store.Grants)
            {
                bool match;
                try { match = predicate(player); }
                catch { match = false; }
                if (match) return true;
            }
            return false;
        }
    }

    /// <summary>Reset the active store. Test-only.</summary>
    public static void Clear()
    {
        var store = Current;
        lock (store.Gate)
        {
            store.Grants.Clear();
        }
    }
}
