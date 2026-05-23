using Majik.Bot.Diagnostics;
using Majik.Core.Api.Dtos;

namespace Majik.Server.Matches;

/// <summary>Wire-format snapshot of a match replay buffer.</summary>
/// <param name="MatchId">Match the replay belongs to.</param>
/// <param name="SealedAt">UTC time the engine→hub bridge detached, if
/// the match has ended. <c>null</c> while the match is still live.</param>
/// <param name="Truncated"><c>true</c> if the per-match cap kicked in and
/// some entries were dropped. Downloaders should warn the user when this
/// is set — the resulting JSON is not a complete game record.</param>
/// <param name="EntryCount">Number of entries actually present in
/// <see cref="Entries"/>. Equal to <c>Entries.Length</c>; surfaced
/// explicitly so callers can sanity-check after JSON deserialization.</param>
/// <param name="Entries">Captured stream in arrival order.</param>
public sealed record MatchReplayDto(
    Guid MatchId,
    DateTime? SealedAt,
    bool Truncated,
    int EntryCount,
    IReadOnlyList<ReplayEntry> Entries);

/// <summary>One captured record in the replay stream — either an engine
/// <see cref="EventDto"/> or a <see cref="BotDecision"/>.</summary>
/// <param name="Seq">Process-monotonic sequence number. Strictly
/// increasing across the entire <see cref="MatchReplayBuffer"/>, so
/// downstream tooling can recover global ordering even if it later
/// merges multiple match replays.</param>
/// <param name="At">UTC capture time at the bridge.</param>
/// <param name="Kind">Discriminator: <c>"event"</c> or <c>"bot-decision"</c>.
/// Stable string — the JSON consumer keys off this.</param>
/// <param name="Event">Set when <see cref="Kind"/> is <c>"event"</c>;
/// <c>null</c> otherwise.</param>
/// <param name="Decision">Set when <see cref="Kind"/> is
/// <c>"bot-decision"</c>; <c>null</c> otherwise.</param>
public sealed record ReplayEntry(
    long Seq,
    DateTime At,
    string Kind,
    EventDto? Event,
    BotDecision? Decision)
{
    public const string KindEvent = "event";
    public const string KindBotDecision = "bot-decision";

    public static ReplayEntry ForEvent(long seq, DateTime at, EventDto evt) =>
        new(seq, at, KindEvent, evt, null);

    public static ReplayEntry ForDecision(long seq, DateTime at, BotDecision decision) =>
        new(seq, at, KindBotDecision, null, decision);
}
