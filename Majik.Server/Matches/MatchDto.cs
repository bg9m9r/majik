namespace Majik.Server.Matches;

/// <summary>Wire format for a match. Live clock balances are
/// computed at serialization time by <see cref="MatchService.ToDto"/>.</summary>
public sealed record MatchDto(
    Guid Id,
    string State,
    string Visibility,
    string Format,
    int ClockMinutes,
    MatchPlayerDto Creator,
    MatchPlayerDto? Opponent,
    MatchRollDto? Roll,
    string? FirstChoice,
    Guid? GameId,
    long CreatorMillisRemaining,
    long OpponentMillisRemaining,
    string? PriorityHolderSub,
    DateTime? PriorityStartedAt,
    string? WinnerSub,
    string? TimeoutLoserSub,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record MatchPlayerDto(string Sub, string Handle, string DeckId, IReadOnlyList<string> DeckSnapshot);
public sealed record MatchRollDto(int? CreatorRoll, int? OpponentRoll, string? WinnerSub);
public sealed record MatchError(string Error, string? Detail = null);

public sealed record CreateMatchRequest(
    string Format,
    string Visibility,
    string DeckId,
    int? ClockMinutes,
    BotOpponentRequest? BotOpponent = null);

/// <summary>Sub-record on <see cref="CreateMatchRequest"/> that, when present,
/// synthesizes an immediate vs-Bot match: the server fills the opponent seat
/// with a <see cref="Majik.Bot.BotPlayerAgent"/> driving the named archetype's
/// deck list. Skips the lobby/roll phases entirely — match enters Playing on
/// creation.</summary>
public sealed record BotOpponentRequest(string Archetype);

/// <summary>One selectable bot archetype: <paramref name="Key"/> is the value
/// posted back in <see cref="BotOpponentRequest.Archetype"/>; <paramref
/// name="Label"/> is the spaced, human-friendly name for the dropdown (e.g.
/// key "BorosEnergy" → label "Boros Energy").</summary>
public sealed record BotArchetypeDto(string Key, string Label);

public sealed record JoinMatchRequest(string DeckId);
public sealed record PlayDrawRequest(string Choice);
