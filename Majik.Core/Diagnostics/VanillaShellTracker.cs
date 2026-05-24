using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Diagnostics;

/// <summary>
/// Game-scoped registry of vanilla-shell card names the bot has already
/// warned about. Prevents the "treat as vanilla shell" warning from spamming
/// the log on every priority pump tick — the engine sees the same card in
/// hand turn after turn, but the operator only needs to be told once per
/// game per card name.
/// <para>
/// Wiring: instantiate per <see cref="Majik.Core.Domain.Aggregates.Game"/>
/// (or per <see cref="Majik.Core.Api.GameFacade"/>), inject into the bot
/// strategy / heuristic agent, and call <see cref="Notice"/> at every
/// decision point that touches a card. First call for a given card name
/// emits an <see cref="UnimplementedCardEncounteredEvent"/> via the bus
/// AND writes a structured WARN line to the supplied logger. Subsequent
/// calls for the same name are silent.
/// </para>
/// <para>
/// Thread-safe — the engine drives priority single-threaded today, but
/// the underlying set + emit step are guarded with a lock so a future
/// multi-agent dispatch (or test harnesses that share the tracker across
/// bot threads) won't double-emit.
/// </para>
/// </summary>
public sealed class VanillaShellTracker
{
    private readonly object _gate = new();
    private readonly HashSet<string> _noticed = new(StringComparer.Ordinal);
    private readonly IEventBus? _eventBus;
    private readonly Action<string>? _logger;

    /// <summary>Number of distinct vanilla-shell card names the tracker has
    /// surfaced this game. Exposed for tests / diagnostics — production
    /// readers should subscribe to the event bus instead.</summary>
    public int NoticedCount
    {
        get { lock (_gate) { return _noticed.Count; } }
    }

    /// <summary>Snapshot of every card name the tracker has surfaced this
    /// game. Stable enumeration order is NOT guaranteed.</summary>
    public IReadOnlyCollection<string> NoticedNames
    {
        get { lock (_gate) { return _noticed.ToList(); } }
    }

    public VanillaShellTracker(IEventBus? eventBus = null, Action<string>? logger = null)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <summary>
    /// Notice that the bot is making a decision involving <paramref name="card"/>.
    /// No-op for cards that aren't vanilla shells, and for cards already
    /// surfaced earlier this game. First-encounter behaviour:
    /// <list type="bullet">
    ///   <item>publish an <see cref="UnimplementedCardEncounteredEvent"/>
    ///   on the supplied event bus (if any),</item>
    ///   <item>write a structured WARN line via the supplied logger (if
    ///   any) — format:
    ///   <c>"WARN: Unimplemented card \"X\" played by Y — treating as vanilla shell. Coverage tier: Unimplemented. Game will continue but EV is unreliable."</c>,</item>
    /// </list>
    /// </summary>
    /// <returns>True iff this call was the first notice for the card's
    /// name (i.e. a warning was emitted). Useful for tests that assert the
    /// once-per-game contract.</returns>
    public bool Notice(ICard card, Player? player, string context)
    {
        if (card is null) return false;
        if (!card.IsVanillaShell) return false;

        bool firstTime;
        lock (_gate)
        {
            firstTime = _noticed.Add(card.Name);
        }
        if (!firstTime) return false;

        var playerLabel = player?.Name ?? "?";
        var msg = $"WARN: Unimplemented card \"{card.Name}\" played by {playerLabel} — "
            + "treating as vanilla shell. Coverage tier: Unimplemented. "
            + "Game will continue but EV is unreliable."
            + (string.IsNullOrEmpty(context) ? "" : $" Context: {context}.");

        try { _logger?.Invoke(msg); }
        catch { /* observer fault must not abort engine */ }

        try { _eventBus?.Publish(new UnimplementedCardEncounteredEvent(card, player, context)); }
        catch { /* observer fault must not abort engine */ }

        return true;
    }

    /// <summary>Test-only: reset the noticed set so a single instance can be
    /// reused across multiple game scenarios. Production callers should
    /// allocate a fresh tracker per game.</summary>
    public void Reset()
    {
        lock (_gate) { _noticed.Clear(); }
    }
}
