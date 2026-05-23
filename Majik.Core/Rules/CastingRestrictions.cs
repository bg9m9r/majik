using Majik.Core.Players;

namespace Majik.Core.Rules;

/// <summary>
/// CR 601.3 — process-level registry for casting restrictions imposed by
/// other game objects (e.g. Teferi, Time Raveler's "Each opponent can cast
/// spells only any time they could cast a sorcery").
///
/// Restrictions are tracked per (player, source-token) so multiple sources
/// can stack without trampling each other; a player is restricted iff at
/// least one entry targeting them is currently registered.
///
/// The registry is a singleton-style static service keyed by reference
/// equality on the source token. <see cref="SorcerySpeedRestrictionEffect"/>
/// is the canonical caller — it registers/unregisters as its source
/// permanent enters/leaves the battlefield via
/// <see cref="Majik.Core.Events.CardMovedEvent"/>.
///
/// <see cref="ActionValidator"/> consults
/// <see cref="MustCastAtSorcerySpeed(Player)"/> during cast validation:
/// when the casting player is restricted and the action's
/// <c>SorcerySpeedAvailable</c> flag is false and the card cannot otherwise
/// be cast at instant speed (e.g. via Flash), the cast is rejected.
///
/// Tests that mutate the registry should call <see cref="Clear"/> in a
/// fixture/dispose path to avoid leakage across cases.
/// </summary>
public static class CastingRestrictions
{
    // Each entry: (token, player). A player is restricted while at least
    // one entry targeting them exists.
    private static readonly List<(object Token, Player Player)> _sorcerySpeed = new();
    private static readonly object _gate = new();

    /// <summary>
    /// Register a sorcery-speed restriction on <paramref name="player"/>,
    /// keyed by <paramref name="token"/>. Idempotent for the same (token,
    /// player) pair — re-registering does not add a second entry.
    /// </summary>
    public static void AddSorcerySpeedRestriction(object token, Player player)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(player);
        lock (_gate)
        {
            foreach (var entry in _sorcerySpeed)
            {
                if (ReferenceEquals(entry.Token, token)
                    && ReferenceEquals(entry.Player, player))
                {
                    return;
                }
            }
            _sorcerySpeed.Add((token, player));
        }
    }

    /// <summary>
    /// Remove every sorcery-speed restriction registered under
    /// <paramref name="token"/> (across all players). Used when a source
    /// permanent leaves the battlefield.
    /// </summary>
    public static void RemoveSorcerySpeedRestriction(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_gate)
        {
            _sorcerySpeed.RemoveAll(e => ReferenceEquals(e.Token, token));
        }
    }

    /// <summary>
    /// True if at least one registered restriction currently requires
    /// <paramref name="player"/> to cast spells only at sorcery speed.
    /// </summary>
    public static bool MustCastAtSorcerySpeed(Player player)
    {
        if (player == null) return false;
        lock (_gate)
        {
            foreach (var entry in _sorcerySpeed)
            {
                if (ReferenceEquals(entry.Player, player)) return true;
            }
            return false;
        }
    }

    /// <summary>Reset the registry. Test-only.</summary>
    public static void Clear()
    {
        lock (_gate)
        {
            _sorcerySpeed.Clear();
        }
    }
}
