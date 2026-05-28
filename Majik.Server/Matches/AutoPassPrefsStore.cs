using System.Collections.Concurrent;
using Majik.Core.Api.Dtos;

namespace Majik.Server.Matches;

/// <summary>
/// Slice 5a — process-local store for per-(matchId, sub) auto-pass
/// preferences. Session-scoped: a server restart resets every entry to
/// the engine's default <see cref="AutoPassPrefs.Default"/>, which is
/// safe (auto-pass starts off-conservative — narrow gate, no stops).
/// Not backed by Mongo on purpose: the prefs are an in-flight UX hint,
/// not gameplay state worth persisting across restarts.
///
/// <para>Thread-safety: ConcurrentDictionary covers concurrent puts +
/// reads. The spec calls out that a prefs PUT can arrive concurrently
/// with a priority-window evaluation; the loop snapshots the prefs
/// once per check, so a mid-evaluation toggle of FullControl yields at
/// most one extra auto-pass before the next window picks up the new
/// value. Acceptable per spec.</para>
///
/// <para>Eviction: terminal-state transitions (Concede / Timeout /
/// Abandon) call <see cref="EvictMatch"/> so the store doesn't grow
/// unbounded across the server's lifetime.</para>
/// </summary>
public sealed class AutoPassPrefsStore
{
    private readonly ConcurrentDictionary<(Guid MatchId, string Sub), AutoPassPrefs> _prefs = new();

    /// <summary>
    /// Replace the prefs entry for <paramref name="matchId"/> +
    /// <paramref name="sub"/>. Idempotent — overwriting an existing
    /// entry is the supported mutation. Setting to
    /// <see cref="AutoPassPrefs.Default"/> is equivalent to having no
    /// entry (the engine's auto-pass gate reads identical behaviour
    /// from "default record" vs "no entry, fall back to default").
    /// </summary>
    public void Set(Guid matchId, string sub, AutoPassPrefs prefs)
    {
        ArgumentNullException.ThrowIfNull(sub);
        ArgumentNullException.ThrowIfNull(prefs);
        _prefs[(matchId, sub)] = prefs;
    }

    /// <summary>
    /// Snapshot read of the prefs for <paramref name="matchId"/> +
    /// <paramref name="sub"/>. Returns <see cref="AutoPassPrefs.Default"/>
    /// when no entry exists (the engine's auto-pass gate will then run
    /// against the conservative default). Null <paramref name="sub"/>
    /// (e.g. resolved seat for which no human is seated) also returns
    /// the default — the caller never has to null-guard.
    /// </summary>
    public AutoPassPrefs Get(Guid matchId, string? sub)
    {
        if (string.IsNullOrEmpty(sub)) return AutoPassPrefs.Default;
        return _prefs.TryGetValue((matchId, sub), out var p) ? p : AutoPassPrefs.Default;
    }

    /// <summary>
    /// Returns <c>true</c> if any entry exists for
    /// (<paramref name="matchId"/>, <paramref name="sub"/>) — used by
    /// the engine wire to distinguish "human seat with default prefs"
    /// from "bot seat / unseated; auto-pass disabled" (the latter
    /// returns null from the prefs provider so PriorityLoop never
    /// short-circuits the bot's own ChoosePriorityActionAsync).
    /// </summary>
    public bool Has(Guid matchId, string? sub)
    {
        if (string.IsNullOrEmpty(sub)) return false;
        return _prefs.ContainsKey((matchId, sub));
    }

    /// <summary>
    /// Evict every entry keyed by <paramref name="matchId"/>. Called
    /// when a match reaches a terminal state (Concede / Timeout /
    /// Abandon) so the store doesn't grow unbounded across server
    /// lifetime. Returns the number of entries removed (useful for
    /// tests + metrics).
    /// </summary>
    public int EvictMatch(Guid matchId)
    {
        var keys = _prefs.Keys.Where(k => k.MatchId == matchId).ToList();
        var removed = 0;
        foreach (var k in keys)
        {
            if (_prefs.TryRemove(k, out _)) removed++;
        }
        return removed;
    }

    /// <summary>Test/diagnostics — total entries across all matches.</summary>
    public int Count => _prefs.Count;
}
