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
/// Three predicate flavours are supported:
/// <list type="bullet">
///   <item><b>Permanent skip</b> — "this permanent doesn't untap during its
///         controller's untap step" (Mana Vault, Stasis-style self-skip).
///         Keyed by (token, permanent); idempotent for the same pair.</item>
///   <item><b>Subtype skip</b> — "permanents with the given subtype don't
///         untap during their controllers' untap steps" (Choke for Islands;
///         later Smoke / Static Orb-adjacent global filters). Symmetric: the
///         predicate fires against any permanent with the subtype regardless
///         of who controls it or whose untap step is current.</item>
///   <item><b>Untap count cap</b> — "players can't untap more than N
///         &lt;filter&gt; during their untap steps" (Static Orb / Winter
///         Orb / Smoke). Enforced per-player by
///         <see cref="ApplyCountCaps(IReadOnlyList{Permanent}, Player)"/>:
///         out of the candidate permanents that match the cap's filter,
///         only the first <c>N</c> are allowed to untap; the rest are
///         tap-locked. Caps stack — when multiple caps apply, the result
///         is the intersection (a permanent is untap-locked if ANY cap
///         excludes it). Each cap carries an <c>IsActive</c> gate so
///         "as long as Static Orb is untapped" conditional caps re-check
///         their source's tap state at consultation time without needing
///         a tap-event surface.</item>
/// </list>
///
/// <see cref="Majik.Core.Game.TurnDriver"/>'s <c>UntapStep</c> consults
/// <see cref="ShouldSkipUntap(Permanent, Player)"/> before untapping each
/// permanent — true => skip — and then asks
/// <see cref="ApplyCountCaps(IReadOnlyList{Permanent}, Player)"/> to thin
/// the remaining candidate list by cap. Sources register on enter-the-
/// battlefield and remove on leave-the-battlefield via lifecycle binders
/// (<see cref="DoesNotUntapStaticEffect"/>, <see cref="SubtypeDoesNotUntapStaticEffect"/>,
/// <see cref="UntapCountCapStaticEffect"/>).
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
    // Count caps: while at least one cap is registered, the untap step's
    // candidate list (after ShouldSkipUntap thinning) is filtered through
    // ApplyCountCaps so any cap's filter selects at most MaxCount survivors.
    // Each cap has an IsActive gate so "as long as <source> is untapped"
    // riders re-check at consultation time without a tap-event surface.
    private static readonly List<UntapCountCap> _countCaps = new();
    private static readonly object _gate = new();

    /// <summary>
    /// Internal record for a count cap. <see cref="IsActive"/> is consulted
    /// at <see cref="ApplyCountCaps"/> time so the cap can gate on the
    /// source's live tap state (Static Orb / Winter Orb "as long as it is
    /// untapped" wording) without needing a TapEvent surface.
    /// </summary>
    public sealed class UntapCountCap
    {
        public object Token { get; }
        public int MaxCount { get; }
        public Func<Permanent, bool> Filter { get; }
        public Func<bool> IsActive { get; }

        public UntapCountCap(object token, int maxCount, Func<Permanent, bool> filter, Func<bool> isActive)
        {
            Token = token ?? throw new ArgumentNullException(nameof(token));
            if (maxCount < 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
            MaxCount = maxCount;
            Filter = filter ?? throw new ArgumentNullException(nameof(filter));
            IsActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
        }
    }

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
    /// Register an "untap at most <paramref name="maxCount"/> permanents
    /// matching <paramref name="filter"/> per untap step" cap (Static Orb,
    /// Winter Orb, Smoke). The <paramref name="isActive"/> gate is consulted
    /// at <see cref="ApplyCountCaps"/> time so conditional caps ("as long
    /// as it is untapped") re-check the source's live tap state without
    /// a TapEvent surface. Idempotent for the same <paramref name="token"/>
    /// — the existing registration is replaced.
    /// </summary>
    public static void MarkUntapCountCap(
        object token,
        int maxCount,
        Func<Permanent, bool> filter,
        Func<bool> isActive)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(isActive);
        lock (_gate)
        {
            _countCaps.RemoveAll(e => ReferenceEquals(e.Token, token));
            _countCaps.Add(new UntapCountCap(token, maxCount, filter, isActive));
        }
    }

    /// <summary>
    /// Remove every untap-skip entry (permanent, subtype, or count cap)
    /// registered under <paramref name="token"/>. Used when the source
    /// permanent leaves the battlefield.
    /// </summary>
    public static void RemoveAll(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_gate)
        {
            _permanentSkips.RemoveAll(e => ReferenceEquals(e.Token, token));
            _subtypeSkips.RemoveAll(e => ReferenceEquals(e.Token, token));
            _countCaps.RemoveAll(e => ReferenceEquals(e.Token, token));
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

    /// <summary>
    /// Apply registered count caps to <paramref name="candidates"/> (the
    /// list of permanents <paramref name="untappingPlayer"/> would untap
    /// this step). For each active cap (its <c>IsActive</c> gate returns
    /// true), at most <c>MaxCount</c> candidates matching the cap's filter
    /// survive — the rest are returned in the "blocked" set. When multiple
    /// caps overlap the result is the intersection (a permanent is blocked
    /// if ANY active cap excludes it).
    ///
    /// v1 selection order: printed iteration order over <paramref name="candidates"/>.
    /// Future hook for an "untap step bot heuristic" can re-order the
    /// candidates before this call (e.g. prefer artifact-mana-source land
    /// over a basic when Winter Orb caps to 1). The cap algorithm itself
    /// is greedy first-fit and order-sensitive.
    /// </summary>
    /// <returns>
    /// Set of permanents that are blocked from untapping by at least one
    /// active count cap. Caller should skip <see cref="Permanent.Untap"/>
    /// for these.
    /// </returns>
    public static HashSet<Permanent> ApplyCountCaps(
        IReadOnlyList<Permanent> candidates,
        Player untappingPlayer)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        _ = untappingPlayer; // v1 caps are symmetric — printed wording is "players"
        var blocked = new HashSet<Permanent>();
        if (candidates.Count == 0) return blocked;

        // Snapshot active caps under lock; evaluate IsActive outside lock
        // so caller-supplied gate predicates can touch any state without
        // re-entering the registry's monitor.
        List<UntapCountCap> snapshot;
        lock (_gate)
        {
            if (_countCaps.Count == 0) return blocked;
            snapshot = new List<UntapCountCap>(_countCaps);
        }

        foreach (var cap in snapshot)
        {
            if (!cap.IsActive()) continue;
            var remaining = cap.MaxCount;
            foreach (var perm in candidates)
            {
                if (!cap.Filter(perm)) continue;
                if (remaining > 0)
                {
                    remaining--;
                }
                else
                {
                    // Cap quota exhausted — this permanent is blocked by
                    // THIS cap. (Other caps may have already allowed it,
                    // but caps stack as intersections.)
                    blocked.Add(perm);
                }
            }
        }

        return blocked;
    }

    /// <summary>True if any count cap is currently registered (regardless
    /// of <c>IsActive</c> state). Used by callers that want to short-circuit
    /// the cap-thinning pass when no caps exist.</summary>
    public static bool HasCountCaps
    {
        get
        {
            lock (_gate) return _countCaps.Count > 0;
        }
    }

    /// <summary>Reset the registry. Test-only.</summary>
    public static void Clear()
    {
        lock (_gate)
        {
            _permanentSkips.Clear();
            _subtypeSkips.Clear();
            _countCaps.Clear();
        }
    }
}
