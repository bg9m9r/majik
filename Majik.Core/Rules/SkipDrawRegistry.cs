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
/// Tests that mutate the registry should call <see cref="Clear"/> in a
/// fixture/dispose path to avoid leakage across cases.
/// </summary>
public static class SkipDrawRegistry
{
    private static readonly List<(object Token, Func<Player, bool> Predicate)> _grants = new();
    private static readonly object _gate = new();

    /// <summary>
    /// Register a skip-draw predicate keyed by <paramref name="token"/>.
    /// Idempotent for the same token — re-registering replaces the prior
    /// predicate so the latest one wins.
    /// </summary>
    public static void AddSkip(object token, Func<Player, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(predicate);
        lock (_gate)
        {
            for (int i = 0; i < _grants.Count; i++)
            {
                if (ReferenceEquals(_grants[i].Token, token))
                {
                    _grants[i] = (token, predicate);
                    return;
                }
            }
            _grants.Add((token, predicate));
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
        lock (_gate)
        {
            _grants.RemoveAll(g => ReferenceEquals(g.Token, token));
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
        lock (_gate)
        {
            foreach (var (_, predicate) in _grants)
            {
                bool match;
                try { match = predicate(player); }
                catch { match = false; }
                if (match) return true;
            }
            return false;
        }
    }

    /// <summary>Reset the registry. Test-only.</summary>
    public static void Clear()
    {
        lock (_gate)
        {
            _grants.Clear();
        }
    }
}
