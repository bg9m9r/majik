using System.Collections.Immutable;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// "Would move zones" intent passed through <see cref="ReplacementBus"/>
/// before the move commits. ETB replacements ("enters tapped",
/// "enters with N counters"), "exile instead of graveyard" replacements,
/// and "if you would draw, instead..." all inspect this.
///
/// <see cref="EntersTapped"/> is a side-channel set by ETB replacements
/// that mutate the card's IsTapped after it lands.
///
/// <see cref="PlusOneCountersOnEnter"/> is the dedicated +1/+1 ETB-counter
/// channel (CR 614.1d). It is kept separate from the generic
/// <see cref="CountersOnEnter"/> bag because +1/+1 counters participate in
/// P/T layering + Hardened Scales' "+1 more" replacement, which reads this
/// field directly. Non-P/T "enters with N counters" loads (charge counters
/// on Everflowing Chalice, etc.) flow through <see cref="CountersOnEnter"/>.
///
/// <see cref="WasCast"/> is true when the card arrived via a normal
/// <see cref="Majik.Core.Game.SpellCastFlow"/> cast (CR 114.1a). It is
/// false for reanimation, blinks, Sneak Attack / Through the Breach
/// cheats, Aether Vial puts, and every other "put onto the battlefield"
/// path. Containment Priest and similar effects consult this flag.
/// </summary>
public sealed record ZoneMoveIntent(
    ICard Card,
    ZoneType FromZone,
    ZoneType ToZone,
    Player? Controller = null,
    bool EntersTapped = false,
    int PlusOneCountersOnEnter = 0,
    bool WasCast = false)
{
    /// <summary>
    /// CR 614.1d — generic "enters the battlefield with N counters of a given
    /// kind" channel for counter types other than +1/+1 (charge, loyalty,
    /// mining, page, …). Accumulated additively by
    /// <see cref="EntersWithCountersReplacement"/> and applied by
    /// <see cref="Majik.Core.Services.ZoneService"/> after the permanent lands
    /// (the same point the <see cref="PlusOneCountersOnEnter"/> +1/+1 channel
    /// is drained), so the permanent enters WITH the counters rather than a
    /// trigger placing them in a separate event afterwards. Empty when no
    /// non-P/T ETB-counter replacement fired.
    /// </summary>
    public ImmutableDictionary<CounterType, int> CountersOnEnter { get; init; } =
        ImmutableDictionary<CounterType, int>.Empty;

    /// <summary>
    /// Returns a copy of this intent with <paramref name="amount"/> more
    /// counters of <paramref name="type"/> queued onto the
    /// <see cref="CountersOnEnter"/> bag (additive — two ETB-counter sources
    /// of the same type accumulate instead of clobbering). Non-positive
    /// amounts are a no-op so a replacement keyed on a dynamic count (e.g.
    /// multikicker × 0) cleanly contributes nothing.
    /// </summary>
    public ZoneMoveIntent WithExtraCounter(CounterType type, int amount)
    {
        if (amount <= 0) return this;
        var current = CountersOnEnter.TryGetValue(type, out var n) ? n : 0;
        return this with { CountersOnEnter = CountersOnEnter.SetItem(type, current + amount) };
    }
}
