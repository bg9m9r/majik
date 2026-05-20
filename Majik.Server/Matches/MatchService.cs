using Majik.Server.Composition;
using Majik.Server.Profiles;

namespace Majik.Server.Matches;

// ---------------------------------------------------------------------------
// Result discriminated union
// ---------------------------------------------------------------------------

public sealed record Result<T>(bool IsSuccess, T? Value, MatchError? Error);

public static class Result
{
    public static Result<T> Ok<T>(T value) => new(true, value, null);
    public static Result<T> Fail<T>(MatchError err) => new(false, default, err);
}

// ---------------------------------------------------------------------------
// MatchService
// ---------------------------------------------------------------------------

/// <summary>
/// Orchestrates the match lifecycle. All state transitions are validated
/// here before being persisted via <see cref="MatchRepository"/>.
/// </summary>
public sealed class MatchService
{
    private static readonly HashSet<int> ValidClockMinutes = new() { 15, 20, 25, 30 };

    private readonly MatchRepository _matches;
    private readonly UserProfileRepository _profiles;
    private readonly DiceRoller _dice;
    private readonly IDeckLoader _decks;
    private readonly IClock _clock;
    private readonly IMatchHubPublisher? _hub;
    private readonly MatchTimeoutScheduler? _timeoutScheduler;
    private readonly ServerGameFactory? _gameFactory;

    public MatchService(
        MatchRepository matches,
        UserProfileRepository profiles,
        DiceRoller dice,
        IDeckLoader decks,
        IClock clock,
        IMatchHubPublisher? hub,
        MatchTimeoutScheduler? timeoutScheduler,
        ServerGameFactory? gameFactory)
    {
        _matches = matches;
        _profiles = profiles;
        _dice = dice;
        _decks = decks;
        _clock = clock;
        _hub = hub;
        _timeoutScheduler = timeoutScheduler;
        _gameFactory = gameFactory;
    }

    // -----------------------------------------------------------------------
    // CreateAsync
    // -----------------------------------------------------------------------

    public async Task<Result<MatchDto>> CreateAsync(
        string callerSub,
        CreateMatchRequest request,
        CancellationToken ct)
    {
        // Validate DeckId
        if (string.IsNullOrWhiteSpace(request.DeckId))
            return Result.Fail<MatchDto>(new MatchError("invalid-request", "DeckId is required."));

        // Validate visibility
        if (!Enum.TryParse<MatchVisibility>(request.Visibility, ignoreCase: true, out var visibility))
            return Result.Fail<MatchDto>(new MatchError("invalid-request", $"Visibility '{request.Visibility}' is not valid."));

        // Resolve and validate clock minutes
        var clockMinutes = request.ClockMinutes ?? 20;
        if (!ValidClockMinutes.Contains(clockMinutes))
            return Result.Fail<MatchDto>(new MatchError("invalid-clock-minutes",
                $"ClockMinutes must be one of: {string.Join(", ", ValidClockMinutes)}."));

        // Lookup caller profile
        var profile = await _profiles.GetBySubAsync(callerSub, ct);
        if (profile is null)
            return Result.Fail<MatchDto>(new MatchError("no-profile", "Caller has no profile."));

        var now = _clock.UtcNow;
        long initialBalance = (long)clockMinutes * 60_000L;

        var match = new Match
        {
            Id = Guid.NewGuid(),
            State = MatchState.Open,
            Visibility = visibility,
            Format = request.Format,
            ClockMinutes = clockMinutes,
            Creator = new MatchPlayer
            {
                Sub = callerSub,
                Handle = profile.HandleDisplay,
                DeckId = request.DeckId,
            },
            Opponent = null,
            CreatorMillisRemaining = initialBalance,
            OpponentMillisRemaining = initialBalance,
            PriorityHolderSub = null,
            PriorityStartedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _matches.InsertAsync(match, ct);

        return Result.Ok(ToDto(match));
    }

    // -----------------------------------------------------------------------
    // ToDto — live-balance helper
    // -----------------------------------------------------------------------

    /// <summary>
    /// Converts a <see cref="Match"/> to its wire DTO, computing live clock
    /// balances: the priority holder's remaining time is decremented by the
    /// elapsed wall time since <see cref="Match.PriorityStartedAt"/> (clamped
    /// to zero). The non-holder's balance is returned as-is.
    /// </summary>
    public MatchDto ToDto(Match m)
    {
        var now = _clock.UtcNow;

        long creatorRemaining = m.CreatorMillisRemaining;
        long opponentRemaining = m.OpponentMillisRemaining;

        if (m.PriorityHolderSub is not null && m.PriorityStartedAt.HasValue)
        {
            var elapsedMs = (long)(now - m.PriorityStartedAt.Value).TotalMilliseconds;
            if (elapsedMs < 0) elapsedMs = 0;

            if (m.PriorityHolderSub == m.Creator.Sub)
                creatorRemaining = Math.Max(0, creatorRemaining - elapsedMs);
            else
                opponentRemaining = Math.Max(0, opponentRemaining - elapsedMs);
        }

        return new MatchDto(
            Id: m.Id,
            State: m.State.ToString(),
            Visibility: m.Visibility.ToString(),
            Format: m.Format,
            ClockMinutes: m.ClockMinutes,
            Creator: new MatchPlayerDto(m.Creator.Sub, m.Creator.Handle, m.Creator.DeckId),
            Opponent: m.Opponent is null
                ? null
                : new MatchPlayerDto(m.Opponent.Sub, m.Opponent.Handle, m.Opponent.DeckId),
            Roll: m.Roll is null
                ? null
                : new MatchRollDto(m.Roll.CreatorRoll, m.Roll.OpponentRoll, m.Roll.WinnerSub),
            FirstChoice: m.FirstChoice,
            GameId: m.GameId,
            CreatorMillisRemaining: creatorRemaining,
            OpponentMillisRemaining: opponentRemaining,
            PriorityHolderSub: m.PriorityHolderSub,
            PriorityStartedAt: m.PriorityStartedAt,
            WinnerSub: m.WinnerSub,
            TimeoutLoserSub: m.TimeoutLoserSub,
            CreatedAt: m.CreatedAt,
            UpdatedAt: m.UpdatedAt);
    }
}
