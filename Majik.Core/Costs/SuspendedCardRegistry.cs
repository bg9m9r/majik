using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.62c–d — tracks cards exiled with time counters by Suspend. At
/// the beginning of each of a tracked card's owner's upkeeps the registry
/// removes one time counter; when a card's counter reaches zero the
/// registry fires the supplied "cast for free" callback so the spell can
/// be put on the stack without paying its mana cost.
///
/// <para>Bookkeeping note: this is a sibling to (not reuse of) a
/// hypothetical Plot registry. Plot doesn't exist in the codebase yet;
/// when it lands it can either share this structure (rename to a generic
/// <c>CastFromExileRegistry</c> keyed by trigger kind) or stay separate.
/// For Suspend specifically the per-upkeep tick + counter bookkeeping is
/// distinctive enough that a focused registry is the clearer shape today.</para>
///
/// <para>Wiring is opt-in: callers pass an <see cref="IEventBus"/> at
/// construction to auto-tick on <see cref="StepStartedEvent"/>(Upkeep), or
/// drive ticks manually via <see cref="TickUpkeep"/> in tests / headless
/// flows.</para>
/// </summary>
public sealed class SuspendedCardRegistry
{
    private sealed class Entry
    {
        public ICard Card { get; }
        public Player Owner { get; }
        public CounterCollection Counters { get; }
        public Action<ICard, Player> OnReady { get; }

        public Entry(ICard card, Player owner, int counters, Action<ICard, Player> onReady)
        {
            Card = card;
            Owner = owner;
            Counters = new CounterCollection();
            if (counters > 0) Counters.Add(CounterType.Time, counters);
            OnReady = onReady;
        }
    }

    private readonly List<Entry> _entries = new();
    private readonly Action<ICard, Player>? _defaultOnReady;
    private readonly IEventBus? _eventBus;

    /// <summary>
    /// Construct a registry with no event-bus wiring. Drive
    /// <see cref="TickUpkeep"/> manually. Cards added via the single-arg
    /// <see cref="Suspend(ICard, Player, int)"/> need
    /// <paramref name="defaultOnReady"/> set, or pass a per-card callback
    /// via the three-arg overload.
    /// </summary>
    public SuspendedCardRegistry(Action<ICard, Player>? defaultOnReady = null)
    {
        _defaultOnReady = defaultOnReady;
        _eventBus = null;
    }

    /// <summary>
    /// Construct a registry subscribed to <paramref name="eventBus"/>.
    /// On each <see cref="StepStartedEvent"/> for the Upkeep step the
    /// registry decrements time counters on every entry whose owner is the
    /// active player (CR 702.62c — "at the beginning of each of your
    /// upkeeps"). Entries reaching zero fire <paramref name="defaultOnReady"/>
    /// (or the per-card callback supplied to <see cref="Suspend(ICard, Player, int, Action{ICard, Player})"/>).
    /// The same bus is used to publish a <see cref="SuspendCounterDrainedEvent"/>
    /// for each entry whose counter reaches zero, BEFORE the ready
    /// callback fires (CR 702.62d) — diagnostic hook independent of the
    /// cast pipeline.
    /// </summary>
    public SuspendedCardRegistry(IEventBus eventBus, Action<ICard, Player>? defaultOnReady = null)
    {
        if (eventBus == null) throw new ArgumentNullException(nameof(eventBus));
        _defaultOnReady = defaultOnReady;
        _eventBus = eventBus;
        eventBus.Subscribe<StepStartedEvent>(e =>
        {
            if (e.StepType == PhaseStateType.Upkeep) TickUpkeep(e.Player);
        });
    }

    /// <summary>Suspend a card using the registry's default ready
    /// callback. Throws if no default callback was supplied at
    /// construction.</summary>
    public void Suspend(ICard card, Player owner, int timeCounters)
    {
        if (_defaultOnReady == null)
            throw new InvalidOperationException(
                "SuspendedCardRegistry has no default ready callback — " +
                "pass an Action<ICard,Player> to the constructor or call " +
                "the Suspend(card, owner, counters, onReady) overload.");
        Suspend(card, owner, timeCounters, _defaultOnReady);
    }

    /// <summary>Suspend a card with an explicit ready callback that fires
    /// when its time counters reach zero. The callback is the integration
    /// point with <see cref="Majik.Core.Game.SpellCastFlow"/> — invoke the
    /// cast pipeline with a zero ManaCost so the spell goes on the stack
    /// without paying its mana cost (CR 702.62d).</summary>
    public void Suspend(ICard card, Player owner, int timeCounters, Action<ICard, Player> onReady)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        if (timeCounters < 0)
            throw new ArgumentOutOfRangeException(nameof(timeCounters), "N ≥ 0.");
        if (onReady == null) throw new ArgumentNullException(nameof(onReady));

        _entries.Add(new Entry(card, owner, timeCounters, onReady));
    }

    /// <summary>Number of time counters currently on the given card, or 0
    /// if the card is not tracked.</summary>
    public int TimeCountersOn(ICard card)
    {
        var e = _entries.FirstOrDefault(x => ReferenceEquals(x.Card, card));
        return e?.Counters.Count(CounterType.Time) ?? 0;
    }

    /// <summary>True if <paramref name="card"/> is currently tracked by the
    /// registry (i.e. exiled with time counters and waiting to be cast).</summary>
    public bool IsTracked(ICard card) =>
        _entries.Any(x => ReferenceEquals(x.Card, card));

    /// <summary>
    /// CR 702.62c — at the beginning of <paramref name="upkeepPlayer"/>'s
    /// upkeep, remove one time counter from each suspended card that
    /// player owns. Cards reaching zero counters are dropped from the
    /// registry and their ready-callback is invoked (CR 702.62d — "When
    /// the last is removed, cast it without paying its mana cost").
    /// </summary>
    public void TickUpkeep(Player upkeepPlayer)
    {
        if (upkeepPlayer == null) throw new ArgumentNullException(nameof(upkeepPlayer));

        // Snapshot to avoid mutation-during-enumeration when an OnReady
        // callback re-enters the registry (rare, but defensive — e.g. a
        // resolved suspended card whose effect suspends another card).
        var owned = _entries
            .Where(e => ReferenceEquals(e.Owner, upkeepPlayer))
            .ToList();

        foreach (var entry in owned)
        {
            var before = entry.Counters.Count(CounterType.Time);
            // CR 702.62c — "remove a time counter". An entry registered
            // with N=0 has nothing to remove; skip it rather than firing
            // the ready-callback on a card that was never really
            // suspended. (Real suspend cards always print N ≥ 1; this
            // guard makes the registry robust against the degenerate
            // case.)
            if (before == 0) continue;

            entry.Counters.Remove(CounterType.Time, 1);
            if (entry.Counters.Count(CounterType.Time) == 0)
            {
                _entries.Remove(entry);
                // CR 702.62d — publish the drain BEFORE the cast callback
                // fires so diagnostics see counter-zero as a discrete step
                // even when the callback throws or short-circuits the cast.
                _eventBus?.Publish(new SuspendCounterDrainedEvent(entry.Card, entry.Owner));
                entry.OnReady(entry.Card, entry.Owner);
            }
        }
    }
}
