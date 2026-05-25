using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Rules;

/// <summary>
/// CR 701.16 / CR 113 — process-level registry mirroring
/// <see cref="PlayerStaticAbilities"/> that tracks "can't be forced to
/// sacrifice" grants on a player. Canonical caller: Sigarda, Host of
/// Herons — "Spells and abilities your opponents control can't cause you
/// to sacrifice permanents."
///
/// Entries are keyed per (player, source-token) so multiple sources can
/// stack independently; a player is protected from forced sacrifice iff
/// at least one entry targeting them is currently registered.
///
/// <para>The registry is consulted by the sacrifice surfaces —
/// <see cref="Majik.Core.Primitives.Fx.Sacrifice(ICard, ICard?)"/> and the
/// edict-shaped spell templates — via
/// <see cref="Player.IsProtectedFromForcedSacrifice(ICard)"/>. The check
/// is "is the requesting source controlled by a player different from the
/// would-be-sacrificer?" — additional costs and the controller's own
/// spells / abilities never trip the gate, only opponent-controlled
/// sources (CR 109.5 "your opponents control").</para>
///
/// <para>The registry is a singleton-style static service keyed by
/// reference equality on the source token. Tests that mutate the registry
/// should call <see cref="Clear"/> in their fixture / dispose path to
/// avoid leakage across cases.</para>
/// </summary>
public static class SacrificeRestriction
{
    // Each entry: (token, player). A player is protected from forced
    // sacrifice while at least one entry targeting them exists.
    private static readonly List<(object Token, Player Player)> _cannotBeForced = new();
    private static readonly object _gate = new();

    /// <summary>
    /// Register a "can't be forced to sacrifice" grant on
    /// <paramref name="target"/>, keyed by reference equality on
    /// <paramref name="source"/>. Idempotent for the same (source, target)
    /// pair — re-registering does not add a second entry. Multiple distinct
    /// sources granting protection to the same player stack without
    /// trampling; <see cref="IsProtectedFromForcedSacrifice"/> returns true
    /// while any entry survives.
    /// </summary>
    public static void AddCannotBeForcedToSacrifice(Player target, ICard source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            foreach (var entry in _cannotBeForced)
            {
                if (ReferenceEquals(entry.Token, source)
                    && ReferenceEquals(entry.Player, target))
                {
                    return;
                }
            }
            _cannotBeForced.Add((source, target));
        }
    }

    /// <summary>
    /// Remove every "can't be forced to sacrifice" grant registered under
    /// <paramref name="source"/> targeting <paramref name="target"/>.
    /// Idempotent — calling for a (source, target) pair that was never
    /// registered (or has already been removed) is a no-op. Used when the
    /// source permanent leaves the battlefield.
    /// </summary>
    public static void RemoveCannotBeForcedToSacrifice(Player target, ICard source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            _cannotBeForced.RemoveAll(e =>
                ReferenceEquals(e.Token, source)
                && ReferenceEquals(e.Player, target));
        }
    }

    /// <summary>
    /// Remove every "can't be forced to sacrifice" grant registered under
    /// <paramref name="source"/> across all players. Useful when the source
    /// LTBs and the caller doesn't want to enumerate which players it was
    /// protecting.
    /// </summary>
    public static void RemoveAllForSource(ICard source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            _cannotBeForced.RemoveAll(e => ReferenceEquals(e.Token, source));
        }
    }

    /// <summary>
    /// True iff <paramref name="player"/> currently has at least one
    /// "can't be forced to sacrifice" grant registered AND the
    /// <paramref name="requestingSource"/> is controlled by a player
    /// different from <paramref name="player"/>. The protected player's
    /// own spells / abilities never trigger the gate; only opponent-
    /// controlled sources do (CR 109.5 "your opponents control").
    ///
    /// <para>When <paramref name="requestingSource"/> is null (sacrifice
    /// driven by a non-card-sourced rules effect — rare) the check defers
    /// to the simple "is anyone protecting this player?" question: the
    /// gate fires.</para>
    /// </summary>
    public static bool IsProtectedFromForcedSacrifice(Player player, ICard? requestingSource)
    {
        if (player == null) return false;
        lock (_gate)
        {
            var protectedAtAll = false;
            foreach (var entry in _cannotBeForced)
            {
                if (ReferenceEquals(entry.Player, player))
                {
                    protectedAtAll = true;
                    break;
                }
            }
            if (!protectedAtAll) return false;
        }

        // Player's own sources never trip the gate (CR 109.5 — "opponents").
        // A null requesting source is treated as "external" — gate fires.
        if (requestingSource?.Controller != null
            && ReferenceEquals(requestingSource.Controller, player))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Overload that keys the gate off the controller of the requesting
    /// source rather than the source card itself. Useful for callers that
    /// only have the controller in hand (e.g. spell-template resolution
    /// bodies that capture <c>ctx.Caster</c> instead of the spell card
    /// itself). Equivalent to <see cref="IsProtectedFromForcedSacrifice(Player, ICard)"/>
    /// with a synthetic source whose controller is
    /// <paramref name="requestingController"/>.
    /// </summary>
    public static bool IsProtectedFromForcedSacrificeBy(Player player, Player? requestingController)
    {
        if (player == null) return false;
        lock (_gate)
        {
            var protectedAtAll = false;
            foreach (var entry in _cannotBeForced)
            {
                if (ReferenceEquals(entry.Player, player))
                {
                    protectedAtAll = true;
                    break;
                }
            }
            if (!protectedAtAll) return false;
        }

        // Self-driven (same controller) never trips the gate.
        if (requestingController != null
            && ReferenceEquals(requestingController, player))
        {
            return false;
        }

        return true;
    }

    /// <summary>Reset the registry. Test-only.</summary>
    public static void Clear()
    {
        lock (_gate) _cannotBeForced.Clear();
    }
}
