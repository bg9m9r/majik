using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// CR 502.1 / CR 614.6 — process-level registry of per-permanent untap-skip
/// filters imposed by other game objects. Sibling of
/// <see cref="Majik.Core.Rules.CastingRestrictions"/>; same source-token
/// shape so multiple sources stack without trampling.
///
/// Two predicate flavours are supported:
/// <list type="bullet">
///   <item><b>Permanent skip</b> — "this permanent doesn't untap during its
///         controller's untap step" (Mana Vault, Stasis-style self-skip).
///         Keyed by (token, permanent); idempotent for the same pair.</item>
///   <item><b>Subtype skip</b> — "permanents with the given subtype don't
///         untap during their controllers' untap steps" (Choke for Islands;
///         later Smoke / Static Orb-adjacent global filters). Symmetric: the
///         predicate fires against any permanent with the subtype regardless
///         of who controls it or whose untap step is current.</item>
/// </list>
///
/// <see cref="Majik.Core.Game.TurnDriver"/>'s <c>UntapStep</c> consults
/// <see cref="ShouldSkipUntap(Permanent, Player)"/> before untapping each
/// permanent — true => skip. Sources register on enter-the-battlefield and
/// remove on leave-the-battlefield via lifecycle binders
/// (<see cref="DoesNotUntapStaticEffect"/>, <see cref="SubtypeDoesNotUntapStaticEffect"/>).
///
/// Tests that mutate the registry should call <see cref="Clear"/> in a
/// fixture/dispose path to avoid leakage across cases.
/// </summary>
public static class UntapStepRestrictions
{
    // Per-permanent skip: while at least one entry targets a permanent, it
    // does not untap. Entries keyed by source token so multiple effects can
    // stack without trampling each other.
    private static readonly List<(object Token, Permanent Target)> _permanentSkips = new();
    // Subtype skip: while at least one entry targets a subtype, every
    // permanent with that subtype is skipped regardless of controller. The
    // token guarantees per-source removability.
    private static readonly List<(object Token, CardSubtype Subtype)> _subtypeSkips = new();
    private static readonly object _gate = new();

    /// <summary>
    /// Register a "<paramref name="permanent"/> doesn't untap during its
    /// controller's untap step" rider, keyed by <paramref name="token"/>.
    /// Idempotent for the same (token, permanent) pair.
    /// </summary>
    public static void MarkPermanentDoesNotUntap(object token, Permanent permanent)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(permanent);
        lock (_gate)
        {
            foreach (var entry in _permanentSkips)
            {
                if (ReferenceEquals(entry.Token, token)
                    && ReferenceEquals(entry.Target, permanent))
                {
                    return;
                }
            }
            _permanentSkips.Add((token, permanent));
        }
    }

    /// <summary>
    /// Register a "permanents with <paramref name="subtype"/> don't untap
    /// during their controllers' untap steps" rider, keyed by
    /// <paramref name="token"/>. Idempotent for the same (token, subtype)
    /// pair.
    /// </summary>
    public static void MarkSubtypeDoesNotUntap(object token, CardSubtype subtype)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_gate)
        {
            foreach (var entry in _subtypeSkips)
            {
                if (ReferenceEquals(entry.Token, token) && entry.Subtype == subtype)
                {
                    return;
                }
            }
            _subtypeSkips.Add((token, subtype));
        }
    }

    /// <summary>
    /// Remove every untap-skip entry (permanent or subtype) registered
    /// under <paramref name="token"/>. Used when the source permanent
    /// leaves the battlefield.
    /// </summary>
    public static void RemoveAll(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_gate)
        {
            _permanentSkips.RemoveAll(e => ReferenceEquals(e.Token, token));
            _subtypeSkips.RemoveAll(e => ReferenceEquals(e.Token, token));
        }
    }

    /// <summary>
    /// True if at least one registered restriction currently prevents
    /// <paramref name="permanent"/> from untapping during
    /// <paramref name="untappingPlayer"/>'s untap step. The
    /// <paramref name="untappingPlayer"/> argument is reserved for future
    /// controller-scoped filters (e.g. "permanents you control don't
    /// untap"); v1 predicates are either self-targeted or symmetric over
    /// a subtype, so the player is informational only.
    /// </summary>
    public static bool ShouldSkipUntap(Permanent permanent, Player untappingPlayer)
    {
        if (permanent == null) return false;
        _ = untappingPlayer; // reserved for future controller-scoped filters
        lock (_gate)
        {
            foreach (var entry in _permanentSkips)
            {
                if (ReferenceEquals(entry.Target, permanent)) return true;
            }
            foreach (var entry in _subtypeSkips)
            {
                if (permanent.HasSubtype(entry.Subtype)) return true;
            }
            return false;
        }
    }

    /// <summary>Reset the registry. Test-only.</summary>
    public static void Clear()
    {
        lock (_gate)
        {
            _permanentSkips.Clear();
            _subtypeSkips.Clear();
        }
    }
}
