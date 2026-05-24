namespace Majik.Core.Counters;

/// <summary>
/// CR 122 — named counter types. P/T-modifying counters (+1/+1, -1/-1)
/// have built-in semantics in the layer system; other types are opaque
/// markers consumed by card-specific abilities (Charge, Loyalty, Quest, …).
/// Free-form string allows cards to introduce new types without changing
/// an enum.
/// </summary>
public sealed record CounterType(string Name)
{
    public static readonly CounterType PlusOnePlusOne = new("+1/+1");
    public static readonly CounterType MinusOneMinusOne = new("-1/-1");

    /// <summary>
    /// CR 122.1g — toughness-only -0/-1 counter (Wall of Roots cost-counter
    /// shape, Phyrexian Furnace's stress cycle, etc.). Power is unaffected;
    /// toughness is reduced by 1 per counter via layer 7c. Does NOT
    /// participate in the +1/+1 / -1/-1 cancellation SBA (CR 704.5q —
    /// that pair-off rule is named to those two types only).
    /// </summary>
    public static readonly CounterType MinusZeroMinusOne = new("-0/-1");
    public static readonly CounterType Loyalty = new("Loyalty");
    public static readonly CounterType Charge = new("Charge");
    public static readonly CounterType Defense = new("Defense");
    public static readonly CounterType Poison = new("Poison");

    /// <summary>
    /// CR 122.1 — Time counters. Used by Suspend (CR 702.62), Vanishing
    /// (CR 702.63), Fading (CR 702.32), and similar timed-exile / fade
    /// mechanics. Each tick removes one counter; reaching zero triggers
    /// the mechanic's payoff (Suspend casts the card without paying its
    /// mana cost).
    /// </summary>
    public static readonly CounterType Time = new("Time");

    /// <summary>
    /// CR 122 — Void counters. Card-specific marker used by Dauthi
    /// Voidwalker (Modern Horizons 2). When an opponent's card would go
    /// to a graveyard, it is exiled with a void counter instead; removing
    /// a void counter is the cost for casting that exiled card without
    /// paying its mana cost.
    /// </summary>
    public static readonly CounterType Void = new("Void");

    /// <summary>
    /// CR 122 — Burden counters. Card-specific marker used by The One Ring
    /// (Tales of Middle-earth). At the beginning of its controller's upkeep
    /// The One Ring's controller loses life equal to the number of burden
    /// counters on it; activating its {T} adds a burden counter and then
    /// draws a card for each burden counter on it.
    /// </summary>
    public static readonly CounterType Burden = new("Burden");

    /// <summary>
    /// CR 122 / CR 106.13 — Energy counter marker for the printed
    /// "Aether Hub enters with an energy counter on it" oracle text
    /// (Kaladesh). Energy as a spendable resource is player-scoped on
    /// <see cref="Majik.Core.Players.Player.EnergyCounters"/>; this
    /// permanent-scoped marker exists only so the on-card counter is
    /// observable on Aether Hub's <see cref="CounterCollection"/> for
    /// inspection / shape tests. The gameplay-relevant gain (controller
    /// gets {E}) happens via <see cref="Majik.Core.Players.Player.GainEnergy"/>
    /// in the ETB effect — the on-card counter is bookkeeping only.
    /// </summary>
    public static readonly CounterType Energy = new("Energy");
}
