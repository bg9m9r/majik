using Majik.Core.Counters;
using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// CR 122 / CR 614.1c — "a counter would be put on a player" intent. The
/// player-scoped twin of <see cref="CounterAddIntent"/> (which covers
/// counters on permanents). Counter placement on a <see cref="Player"/>
/// (poison — CR 122 / CR 704.5c; energy — CR 107.16; experience —
/// CR 107.14; or any generic player counter) is routed through the
/// player's attached <see cref="ReplacementBus"/> via
/// <see cref="Majik.Core.Services.PlayerCountersService.Add"/> before the
/// count commits, so CR 614 replacement effects can:
/// <list type="bullet">
///   <item>scale the placement,</item>
///   <item>rewrite <see cref="Amount"/> to 0 — the "can't get counters"
///         shape used by Solemnity ("players can't get counters") and
///         Suncleanser ("that player can't get counters for as long as
///         this creature remains on the battlefield"),</item>
///   <item>cancel it outright (returning <c>null</c>).</item>
/// </list>
/// Players without an attached bus keep the direct-mutation path — every
/// pre-existing caller of <see cref="Player.AddPoisonCounters"/> /
/// <see cref="Player.GainEnergy"/> works unchanged in the unwired posture.
/// </summary>
public sealed record PlayerCounterAddIntent(
    Player Target,
    CounterType Type,
    int Amount);
