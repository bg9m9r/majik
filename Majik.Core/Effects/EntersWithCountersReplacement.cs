using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614.1d — "this permanent enters the battlefield with N counters of a
/// given kind on it" replacement. Watches the card's own ETB
/// <see cref="ZoneMoveIntent"/> and queues the counters onto the intent so
/// <see cref="Services.ZoneService"/> places them as the permanent lands —
/// the permanent enters WITH the counters (no separate trigger / event after
/// the fact, so there is never a window where it sits on the battlefield with
/// the wrong count, and other ETB-counter replacements observe it correctly).
///
/// <para>
/// +1/+1 counters route through the dedicated
/// <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> channel so they keep
/// participating in P/T layering and Hardened Scales' "+1 more" bump. Every
/// other counter type (charge, mining, page, …) flows through the generic
/// <see cref="ZoneMoveIntent.CountersOnEnter"/> bag.
/// </para>
///
/// <para>
/// Covers fixed loads — Strangleroot Geist (one +1/+1), Triskelion (three),
/// Spike Feeder (two), Modular inheritance — and dynamic loads via the
/// <see cref="Func{TResult}"/> count overload: Everflowing Chalice enters with
/// a charge counter for each time it was multikicked (CR 702.32c), reading
/// <see cref="Card.TimesKicked"/> at entry. Variable-X variants (Walking
/// Ballista's {X}) thread <c>ChosenSpellParams.X</c> into a similar dynamic
/// count.
/// </para>
/// </summary>
public sealed class EntersWithCountersReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly ICard _card;
    private readonly CounterType _type;
    private readonly Func<int> _amount;

    /// <summary>
    /// Fixed +1/+1 count — the original shape, preserved so existing callers
    /// (Spike Feeder, Servant of the Scale, Modular) compile unchanged.
    /// </summary>
    public EntersWithCountersReplacement(ICard card, int amount)
        : this(card, CounterType.PlusOnePlusOne, amount)
    {
    }

    /// <summary>Fixed count of an arbitrary counter type.</summary>
    public EntersWithCountersReplacement(ICard card, CounterType type, int amount)
        : this(card, type, () => amount)
    {
    }

    /// <summary>
    /// Dynamic count of an arbitrary counter type — the count is evaluated
    /// once, lazily, when the ETB intent is being replaced (CR 614.1d /
    /// CR 702.32c). Everflowing Chalice keys this on
    /// <see cref="Card.TimesKicked"/>.
    /// </summary>
    public EntersWithCountersReplacement(ICard card, CounterType type, Func<int> amount)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _type = type ?? throw new ArgumentNullException(nameof(type));
        _amount = amount ?? throw new ArgumentNullException(nameof(amount));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        ReferenceEquals(intent.Card, _card)
        && intent.ToZone == ZoneType.Battlefield
        && intent.FromZone != ZoneType.Battlefield;

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        var n = _amount();
        if (n <= 0) return intent;

        // +1/+1 keeps its dedicated channel (P/T layering + Hardened Scales
        // observation); every other type queues onto the generic bag.
        // Additive — a card stacking two ETB-counter sources of the same type
        // accumulates instead of clobbering.
        return _type == CounterType.PlusOnePlusOne
            ? intent with { PlusOneCountersOnEnter = intent.PlusOneCountersOnEnter + n }
            : intent.WithExtraCounter(_type, n);
    }
}
