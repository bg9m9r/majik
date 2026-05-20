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
public sealed record MatchRollDto(int CreatorRoll, int OpponentRoll, string WinnerSub);
public sealed record MatchError(string Error, string? Detail = null);

public sealed record CreateMatchRequest(
    string Format,
    string Visibility,
    string DeckId,
    int? ClockMinutes);

public sealed record JoinMatchRequest(string DeckId);
public sealed record PlayDrawRequest(string Choice);
