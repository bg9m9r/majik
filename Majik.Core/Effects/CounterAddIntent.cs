using Majik.Core.Cards;
using Majik.Core.Counters;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614.1c — "would-place-counters" intent. Effects like Hardened Scales
/// ("If one or more +1/+1 counters would be put on a creature you control,
/// that many plus one +1/+1 counters are put on it instead") inspect this
/// and increase <see cref="Amount"/>. Callers route counter placement
/// through <see cref="ReplacementBus"/> first, then commit by adding to
/// the permanent's <see cref="Permanent.Counters"/>.
/// </summary>
public sealed record CounterAddIntent(
    Permanent Target,
    CounterType Type,
    int Amount);
