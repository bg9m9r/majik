using Majik.Core.Cards;
using Majik.Core.Counters;

namespace Majik.Core.Events;

/// <summary>
/// CR 121 / CR 614 — fired by <see cref="Majik.Core.Services.CountersService.Add"/>
/// after one or more counters have been placed on a permanent. Published
/// AFTER all replacement effects (Hardened Scales, Doubling Season, etc.)
/// have been applied to the original intent, so <see cref="Amount"/>
/// reflects the actual count committed to the target's
/// <see cref="Permanent.Counters"/>.
///
/// <para>
/// This is the surface "Whenever one or more +1/+1 counters are put on a
/// permanent you control, …" triggers (Animation Module, Conclave Mentor,
/// Winding Constrictor's symmetric rider) subscribe to. The triggering
/// player (a.k.a. "you" in the printed text) is the target's controller
/// at the moment of the placement — carried as <see cref="Controller"/>.
/// </para>
///
/// <para>
/// CR 603.6c — the trigger sees the post-replacement amount, so an
/// Animation-Module-style "may pay {1}" rider fires exactly once per
/// CountersService.Add call regardless of how many counters landed
/// (printed text says "Whenever one or more +1/+1 counters are put on
/// …" — a single event captures the whole placement). Per-counter
/// triggers ("for each counter you put on …") would consume
/// <see cref="Amount"/> as a multiplier; Animation Module ignores it
/// (the "you may pay {1}" question fires once per event regardless).
/// </para>
/// </summary>
public class CounterAddedEvent : GameEvent
{
    /// <summary>The permanent that received the counters.</summary>
    public Permanent Target { get; }

    /// <summary>The kind of counter placed (e.g. +1/+1, charge, loyalty).</summary>
    public CounterType CounterType { get; }

    /// <summary>Post-replacement amount actually committed to the target's
    /// counter collection. Always &gt; 0 (the event is only published
    /// when a non-zero placement landed).</summary>
    public int Amount { get; }

    /// <summary>The target's controller at the moment of the placement —
    /// the canonical "you" for "permanent you control" predicates. Lifted
    /// from <see cref="Permanent.Controller"/> at publish time; null when
    /// the permanent has no controller (shouldn't happen for live
    /// battlefield permanents but defended against).</summary>
    public Players.Player? Controller { get; }

    public CounterAddedEvent(Permanent target, CounterType type, int amount)
        : base(EventType.CounterAdded)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        CounterType = type ?? throw new ArgumentNullException(nameof(type));
        Amount = amount;
        Controller = target.Controller;
    }
}
