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
}
