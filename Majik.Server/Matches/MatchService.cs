using Majik.Server.Composition;
using Majik.Server.Profiles;
using MongoDB.Driver;

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
    // JoinAsync
    // -----------------------------------------------------------------------

    public async Task<Result<MatchDto>> JoinAsync(
        string callerSub,
        Guid matchId,
        JoinMatchRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DeckId))
            return Result.Fail<MatchDto>(new MatchError("invalid-request", "deckId"));

        var match = await _matches.GetByIdAsync(matchId, ct);
        if (match == null) return Result.Fail<MatchDto>(new MatchError("match-not-found"));
        if (match.Creator.Sub == callerSub)
            return Result.Fail<MatchDto>(new MatchError("self-join-forbidden"));
        if (match.State != MatchState.Open)
            return Result.Fail<MatchDto>(new MatchError("match-not-open"));

        var profile = await _profiles.GetBySubAsync(callerSub, ct);
        if (profile == null) return Result.Fail<MatchDto>(new MatchError("no-profile"));

        var opponent = new MatchPlayer
        {
            Sub = callerSub,
            Handle = profile.HandleDisplay,
            DeckId = request.DeckId.Trim(),
        };

        var now = _clock.UtcNow;
        var setOpponent = Builders<Match>.Update
            .Set(m => m.Opponent, opponent)
            .Set(m => m.State, MatchState.Joined)
            .Set(m => m.UpdatedAt, now);

        var won = await _matches.TryAtomicUpdateAsync(matchId, MatchState.Open, setOpponent, ct);
        if (!won) return Result.Fail<MatchDto>(new MatchError("match-not-open"));

        _hub?.Publish(matchId, "match.opponent-joined",
            new { matchId, opponent = new MatchPlayerDto(opponent.Sub, opponent.Handle, opponent.DeckId) });

        // Create engine game (decks + facade)
        if (_gameFactory != null)
        {
            var creatorDeck = await _decks.LoadAsync(match.Creator.DeckId, ct);
            var opponentDeck = await _decks.LoadAsync(opponent.DeckId, ct);
            var facade = _gameFactory.Create(match.Creator.Handle, opponent.Handle, creatorDeck, opponentDeck);
            await _matches.TryAtomicUpdateAsync(matchId, MatchState.Joined,
                Builders<Match>.Update.Set(m => m.GameId, facade.GameId),
                ct);
        }

        // Transition Joined → Starting → Rolling
        await TransitionStateAsync(matchId, MatchState.Joined, MatchState.Starting, now, ct);
        await TransitionStateAsync(matchId, MatchState.Starting, MatchState.Rolling, now, ct);

        // Roll dice + persist
        var roll = _dice.Roll(match.Creator.Sub, opponent.Sub);
        var setRoll = Builders<Match>.Update
            .Set(m => m.Roll, roll)
            .Set(m => m.UpdatedAt, _clock.UtcNow);
        await _matches.TryAtomicUpdateAsync(matchId, MatchState.Rolling, setRoll, ct);

        _hub?.Publish(matchId, "match.rolled",
            new { matchId, roll = new MatchRollDto(roll.CreatorRoll, roll.OpponentRoll, roll.WinnerSub) });

        var fresh = (await _matches.GetByIdAsync(matchId, ct))!;
        return Result.Ok(ToDto(fresh));
    }

    private async Task TransitionStateAsync(Guid id, MatchState from, MatchState to, DateTime now, CancellationToken ct)
    {
        var update = Builders<Match>.Update
            .Set(m => m.State, to)
            .Set(m => m.UpdatedAt, now);
        var moved = await _matches.TryAtomicUpdateAsync(id, from, update, ct);
        if (moved)
        {
            _hub?.Publish(id, "match.state-changed",
                new { matchId = id, state = to.ToString(), transitionedAt = now });
        }
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
