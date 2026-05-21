using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Server.Composition;
using Majik.Server.Decks;
using Majik.Server.Profiles;
using Microsoft.Extensions.Logging;
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
    private readonly DeckRepository? _deckRepo;
    private readonly DeckValidationService? _deckValidator;
    private readonly ILogger<MatchService>? _logger;

    public MatchService(
        MatchRepository matches,
        UserProfileRepository profiles,
        DiceRoller dice,
        IDeckLoader decks,
        IClock clock,
        IMatchHubPublisher? hub,
        MatchTimeoutScheduler? timeoutScheduler,
        ServerGameFactory? gameFactory,
        DeckRepository? deckRepo = null,
        DeckValidationService? deckValidator = null,
        ILogger<MatchService>? logger = null)
    {
        _matches = matches;
        _profiles = profiles;
        _dice = dice;
        _decks = decks;
        _clock = clock;
        _hub = hub;
        _timeoutScheduler = timeoutScheduler;
        _gameFactory = gameFactory;
        _deckRepo = deckRepo;
        _deckValidator = deckValidator;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // ResolveDeckSnapshotAsync
    // -----------------------------------------------------------------------

    private async Task<(IReadOnlyList<string> snapshot, MatchError? error)>
        ResolveDeckSnapshotAsync(string ownerSub, string deckId, CancellationToken ct)
    {
        if (_deckRepo == null || _deckValidator == null)
        {
            return (Array.Empty<string>(), null); // legacy stub path — match still works
        }

        if (!Guid.TryParse(deckId, out var id))
        {
            return (Array.Empty<string>(), new MatchError("deck-not-found"));
        }

        var deck = await _deckRepo.GetByIdForOwnerAsync(id, ownerSub, ct);
        if (deck == null)
        {
            return (Array.Empty<string>(), new MatchError("deck-not-found"));
        }

        var validation = _deckValidator.Validate(deck);
        if (!validation.IsValid)
        {
            return (Array.Empty<string>(), new MatchError("deck-invalid",
                string.Join("; ", validation.Errors)));
        }

        var snapshot = deck.Mainboard
            .SelectMany(e => Enumerable.Repeat(e.Name, e.Count))
            .ToList();
        return (snapshot, null);
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

        var (creatorSnapshot, deckErr) = await ResolveDeckSnapshotAsync(callerSub, request.DeckId, ct);
        if (deckErr != null) return Result.Fail<MatchDto>(deckErr);

        var now = _clock.UtcNow;
        long initialBalance = (long)clockMinutes * 60_000L;
        var creator = new MatchPlayer
        {
            Sub = callerSub,
            Handle = profile.HandleDisplay,
            DeckId = request.DeckId,
            DeckSnapshot = creatorSnapshot.ToList(),
        };

        // vs-Bot branch: synthesize an opponent seat, skip lobby/roll, and
        // hand the game straight to a BotPlayerAgent. Bot matches are
        // always Invite-scoped (they never list in the public lobby).
        if (request.BotOpponent is { } bot)
        {
            return await CreateBotMatchAsync(creator, request, bot, clockMinutes, initialBalance, now, ct);
        }

        var match = new Match
        {
            Id = Guid.NewGuid(),
            State = MatchState.Open,
            Visibility = visibility,
            Format = request.Format,
            ClockMinutes = clockMinutes,
            Creator = creator,
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
    // CreateBotMatchAsync — vs-Bot branch of CreateAsync.
    //
    // Differences from the human-vs-human path:
    //   * Opponent seat is synthesized in-process (Sub = "bot:<archetype>");
    //     no second-player join, no roll.
    //   * Visibility is forced to Invite so bot matches never surface in the
    //     public lobby listing.
    //   * State transitions go straight Open → Joined → Starting → Playing,
    //     bypassing Rolling entirely. Bot always sits on the Opponent (Bob)
    //     seat; creator is the first player.
    //   * GameFacade is wired with botSeatArchetype so the engine gets a
    //     BotPlayerAgent on the Bob seat from the moment the game starts.
    // -----------------------------------------------------------------------
    private async Task<Result<MatchDto>> CreateBotMatchAsync(
        MatchPlayer creator,
        CreateMatchRequest request,
        BotOpponentRequest bot,
        int clockMinutes,
        long initialBalance,
        DateTime now,
        CancellationToken ct)
    {
        if (!Majik.Bot.Decks.BotDeckCatalog.Archetypes.Contains(bot.Archetype))
            return Result.Fail<MatchDto>(new MatchError("invalid-request",
                $"Unknown bot archetype '{bot.Archetype}'."));

        var botSnapshot = Majik.Bot.Decks.BotDeckCatalog.Get(bot.Archetype).ToList();
        var botPlayer = new MatchPlayer
        {
            Sub = $"bot:{bot.Archetype}",
            Handle = Majik.Bot.Decks.BotDeckCatalog.DisplayName(bot.Archetype),
            DeckId = $"bot:{bot.Archetype}",
            DeckSnapshot = botSnapshot,
        };

        // 1) Load decks + create facade BEFORE any DB write so a
        //    DeckLoadException cannot leave an orphan Match document.
        GameFacade? facade = null;
        if (_gameFactory != null)
        {
            try
            {
                var creatorDeck = await _decks.LoadAsync(creator.DeckId, ct);
                var botDeck = await _decks.LoadFromCardNamesAsync(botSnapshot, ct);
                facade = _gameFactory.Create(
                    creator.Handle, botPlayer.Handle,
                    creatorDeck, botDeck,
                    botSeatArchetype: bot.Archetype);
            }
            catch (DeckLoadException ex)
            {
                return Result.Fail<MatchDto>(new MatchError("deck-invalid", ex.Message));
            }
        }

        var matchId = Guid.NewGuid();
        var match = new Match
        {
            Id = matchId,
            // Insert as Open so the same Joined/Starting transition
            // machinery (TransitionStateAsync with CAS-on-previous) works
            // unchanged for the bot path.
            State = MatchState.Open,
            Visibility = MatchVisibility.Invite,
            Format = request.Format,
            ClockMinutes = clockMinutes,
            Creator = creator,
            Opponent = botPlayer,
            // GameId is set up-front now that the facade is created first;
            // this eliminates a separate post-Insert CAS round-trip.
            GameId = facade?.GameId,
            CreatorMillisRemaining = initialBalance,
            OpponentMillisRemaining = initialBalance,
            PriorityHolderSub = null,
            PriorityStartedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // 2) Insert + transitions are wrapped so any CAS conflict or
        //    Mongo failure cleans up both the match doc and the facade.
        Match fresh;
        try
        {
            await _matches.InsertAsync(match, ct);

            // Open → Joined → Starting → Playing. Skip Rolling: the bot doesn't
            // roll and the creator always plays first.
            if (!await TryTransitionStateAsync(matchId, MatchState.Open, MatchState.Joined, now, ct))
                throw new InvalidOperationException("CAS conflict during bot match setup (Open→Joined).");
            if (!await TryTransitionStateAsync(matchId, MatchState.Joined, MatchState.Starting, now, ct))
                throw new InvalidOperationException("CAS conflict during bot match setup (Joined→Starting).");

            // Mirror PlayDrawAsync's into-Playing transition: set priority holder
            // + start clock for the creator, then kick the engine.
            var setPlaying = Builders<Match>.Update
                .Set(m => m.FirstChoice, "play")
                .Set(m => m.State, MatchState.Playing)
                .Set(m => m.PriorityHolderSub, creator.Sub)
                .Set(m => m.PriorityStartedAt, now)
                .Set(m => m.UpdatedAt, now);
            if (!await _matches.TryAtomicUpdateAsync(matchId, MatchState.Starting, setPlaying, ct))
                throw new InvalidOperationException("CAS conflict during bot match setup (Starting→Playing).");

            fresh = (await _matches.GetByIdAsync(matchId, ct))!;
        }
        catch (Exception ex)
        {
            // Best-effort cleanup: delete the match doc and the facade so we
            // don't leave a partial-state record behind.
            try
            {
                await _matches.DeleteByIdAsync(matchId, ct);
            }
            catch (Exception delEx)
            {
                _logger?.LogError(delEx,
                    "Failed to delete match doc during bot-match setup cleanup. MatchId={MatchId}",
                    matchId);
            }
            if (_gameFactory != null && facade != null)
            {
                _gameFactory.Delete(facade.GameId);
            }
            _logger?.LogError(ex,
                "Bot match setup failed; rolled back. MatchId={MatchId}", matchId);
            return Result.Fail<MatchDto>(new MatchError("internal",
                "Bot match setup failed."));
        }

        // 3) Fire-and-forget the engine startup, but log faults instead of
        //    swallowing them so a dead engine doesn't masquerade as Playing.
        if (facade != null)
        {
            _ = facade.StartFullGameAsync(firstPlayerSlot: 0)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        _logger?.LogError(t.Exception,
                            "Bot match engine faulted at startup. MatchId={MatchId}", matchId);
                    else if (t.IsCanceled)
                        _logger?.LogWarning(
                            "Bot match engine canceled at startup. MatchId={MatchId}", matchId);
                }, TaskScheduler.Default);
        }
        _timeoutScheduler?.Schedule(matchId, creator.Sub, clockMinutes * 60_000L);
        _hub?.Publish(matchId, "match.state-changed",
            new { matchId, state = "Playing", transitionedAt = now });

        return Result.Ok(ToDto(fresh));
    }

    // Variant of TransitionStateAsync that surfaces the CAS result so callers
    // can fail loudly instead of silently proceeding on a missed transition.
    private async Task<bool> TryTransitionStateAsync(
        Guid id, MatchState from, MatchState to, DateTime now, CancellationToken ct)
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
        return moved;
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

        var (opponentSnapshot, deckErr) = await ResolveDeckSnapshotAsync(callerSub, request.DeckId, ct);
        if (deckErr != null) return Result.Fail<MatchDto>(deckErr);

        var opponent = new MatchPlayer
        {
            Sub = callerSub,
            Handle = profile.HandleDisplay,
            DeckId = request.DeckId.Trim(),
            DeckSnapshot = opponentSnapshot.ToList(),
        };

        var now = _clock.UtcNow;
        var setOpponent = Builders<Match>.Update
            .Set(m => m.Opponent, opponent)
            .Set(m => m.State, MatchState.Joined)
            .Set(m => m.UpdatedAt, now);

        var won = await _matches.TryAtomicUpdateAsync(matchId, MatchState.Open, setOpponent, ct);
        if (!won) return Result.Fail<MatchDto>(new MatchError("match-not-open"));

        _hub?.Publish(matchId, "match.opponent-joined",
            new { matchId, opponent = new MatchPlayerDto(opponent.Sub, opponent.Handle, opponent.DeckId, new List<string>()) });

        // Create engine game (decks + facade)
        if (_gameFactory != null)
        {
            try
            {
                var creatorDeck = await _decks.LoadAsync(match.Creator.DeckId, ct);
                var opponentDeck = await _decks.LoadAsync(opponent.DeckId, ct);
                var facade = _gameFactory.Create(match.Creator.Handle, opponent.Handle, creatorDeck, opponentDeck);
                await _matches.TryAtomicUpdateAsync(matchId, MatchState.Joined,
                    Builders<Match>.Update.Set(m => m.GameId, facade.GameId),
                    ct);
            }
            catch (DeckLoadException ex)
            {
                return Result.Fail<MatchDto>(new MatchError("deck-invalid", ex.Message));
            }
        }

        // Transition Joined → Starting → Rolling
        await TransitionStateAsync(matchId, MatchState.Joined, MatchState.Starting, now, ct);
        await TransitionStateAsync(matchId, MatchState.Starting, MatchState.Rolling, now, ct);

        // Initialize empty roll record; per-player rolls land via SubmitRollAsync.
        var setRoll = Builders<Match>.Update
            .Set(m => m.Roll, new MatchRoll())
            .Set(m => m.UpdatedAt, _clock.UtcNow);
        await _matches.TryAtomicUpdateAsync(matchId, MatchState.Rolling, setRoll, ct);

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
    // PlayDrawAsync
    // -----------------------------------------------------------------------

    public async Task<Result<MatchDto>> PlayDrawAsync(
        string callerSub, Guid matchId, PlayDrawRequest request, CancellationToken ct)
    {
        var match = await _matches.GetByIdAsync(matchId, ct);
        if (match == null) return Result.Fail<MatchDto>(new MatchError("match-not-found"));
        if (match.State != MatchState.Rolling) return Result.Fail<MatchDto>(new MatchError("not-rolling"));
        if (match.Roll == null || match.Roll.WinnerSub != callerSub)
            return Result.Fail<MatchDto>(new MatchError("not-roll-winner"));

        var choice = request.Choice?.ToLowerInvariant();
        if (choice != "play" && choice != "draw")
            return Result.Fail<MatchDto>(new MatchError("invalid-choice"));

        string firstPlayerSub = choice == "play" ? callerSub
            : callerSub == match.Creator.Sub ? match.Opponent!.Sub : match.Creator.Sub;
        int firstPlayerSlot = firstPlayerSub == match.Creator.Sub ? 0 : 1;

        var now = _clock.UtcNow;
        var update = Builders<Match>.Update
            .Set(m => m.FirstChoice, choice)
            .Set(m => m.State, MatchState.Playing)
            .Set(m => m.PriorityHolderSub, firstPlayerSub)
            .Set(m => m.PriorityStartedAt, now)
            .Set(m => m.UpdatedAt, now);

        var moved = await _matches.TryAtomicUpdateAsync(matchId, MatchState.Rolling, update, ct);
        if (!moved) return Result.Fail<MatchDto>(new MatchError("not-rolling"));

        if (_gameFactory != null && match.GameId is Guid gid)
        {
            var facade = _gameFactory.Get(gid);
            facade?.StartFullGameAsync(firstPlayerSlot);
        }
        _timeoutScheduler?.Schedule(matchId, firstPlayerSub, match.ClockMinutes * 60_000L);
        _hub?.Publish(matchId, "match.play-draw-chosen",
            new { matchId, choice, firstPlayerSub });
        _hub?.Publish(matchId, "match.state-changed",
            new { matchId, state = "Playing", transitionedAt = now });
        _hub?.Publish(matchId, "match.clock-update",
            new { matchId, creatorMs = match.CreatorMillisRemaining, opponentMs = match.OpponentMillisRemaining, holder = firstPlayerSub, startedAt = now });

        var fresh = (await _matches.GetByIdAsync(matchId, ct))!;
        return Result.Ok(ToDto(fresh));
    }

    // -----------------------------------------------------------------------
    // SubmitRollAsync
    // -----------------------------------------------------------------------

    public async Task<Result<MatchDto>> SubmitRollAsync(string callerSub, Guid matchId, CancellationToken ct)
    {
        const int MaxTieRetries = 100;

        var match = await _matches.GetByIdAsync(matchId, ct);
        if (match == null) return Result.Fail<MatchDto>(new MatchError("match-not-found"));
        if (match.State != MatchState.Rolling) return Result.Fail<MatchDto>(new MatchError("not-rolling"));

        bool isCreator = callerSub == match.Creator.Sub;
        bool isOpponent = match.Opponent != null && callerSub == match.Opponent.Sub;
        if (!isCreator && !isOpponent) return Result.Fail<MatchDto>(new MatchError("not-a-player"));

        var roll = match.Roll ?? new MatchRoll();

        // Idempotent: if caller's slot already set, just return current snapshot
        var callerSlotFilled = isCreator ? roll.CreatorRoll.HasValue : roll.OpponentRoll.HasValue;
        if (callerSlotFilled)
        {
            return Result.Ok(ToDto(match));
        }

        // Generate this player's roll
        int value = _dice.RollSingle();
        if (isCreator) roll.CreatorRoll = value;
        else roll.OpponentRoll = value;

        // If both filled, resolve winner (with tie auto-reroll)
        if (roll.CreatorRoll.HasValue && roll.OpponentRoll.HasValue)
        {
            int retries = 0;
            while (roll.CreatorRoll!.Value == roll.OpponentRoll!.Value)
            {
                if (++retries > MaxTieRetries)
                    throw new InvalidOperationException("Tie reroll cap exceeded — random source likely broken.");
                roll.CreatorRoll = _dice.RollSingle();
                roll.OpponentRoll = _dice.RollSingle();
            }
            roll.WinnerSub = roll.CreatorRoll.Value > roll.OpponentRoll.Value
                ? match.Creator.Sub : match.Opponent!.Sub;
        }

        var now = _clock.UtcNow;
        var update = Builders<Match>.Update
            .Set(m => m.Roll, roll)
            .Set(m => m.UpdatedAt, now);
        var moved = await _matches.TryAtomicUpdateAsync(matchId, MatchState.Rolling, update, ct);
        if (!moved) return Result.Fail<MatchDto>(new MatchError("not-rolling"));

        // Publish per-player event for this caller's roll
        _hub?.Publish(matchId, "match.player-rolled", new { matchId, sub = callerSub, roll = value });

        // If winner determined, publish consolidated match.rolled event
        if (roll.WinnerSub != null)
        {
            _hub?.Publish(matchId, "match.rolled",
                new { matchId, roll = new MatchRollDto(roll.CreatorRoll, roll.OpponentRoll, roll.WinnerSub) });
        }

        var fresh = (await _matches.GetByIdAsync(matchId, ct))!;
        return Result.Ok(ToDto(fresh));
    }

    // -----------------------------------------------------------------------
    // ConcedeAsync
    // -----------------------------------------------------------------------

    public async Task<Result<MatchDto>> ConcedeAsync(string callerSub, Guid matchId, CancellationToken ct)
    {
        var match = await _matches.GetByIdAsync(matchId, ct);
        if (match == null) return Result.Fail<MatchDto>(new MatchError("match-not-found"));
        var isParty = callerSub == match.Creator.Sub || callerSub == match.Opponent?.Sub;
        if (!isParty) return Result.Fail<MatchDto>(new MatchError("forbidden"));
        if (match.State != MatchState.Playing)
            return Result.Fail<MatchDto>(new MatchError("cannot-concede"));

        var winner = callerSub == match.Creator.Sub ? match.Opponent!.Sub : match.Creator.Sub;
        var now = _clock.UtcNow;
        var update = Builders<Match>.Update
            .Set(m => m.State, MatchState.Completed)
            .Set(m => m.WinnerSub, winner)
            .Set(m => m.UpdatedAt, now);

        var moved = await _matches.TryAtomicUpdateAsync(matchId, MatchState.Playing, update, ct);
        if (!moved) return Result.Fail<MatchDto>(new MatchError("cannot-concede"));

        _timeoutScheduler?.Cancel(matchId);
        _hub?.Publish(matchId, "match.state-changed",
            new { matchId, state = "Completed", transitionedAt = now });

        var fresh = (await _matches.GetByIdAsync(matchId, ct))!;
        return Result.Ok(ToDto(fresh));
    }

    // -----------------------------------------------------------------------
    // AbandonAsync
    // -----------------------------------------------------------------------

    public async Task<Result<bool>> AbandonAsync(string callerSub, Guid matchId, CancellationToken ct)
    {
        var match = await _matches.GetByIdAsync(matchId, ct);
        if (match == null) return Result.Fail<bool>(new MatchError("match-not-found"));
        if (match.Creator.Sub != callerSub) return Result.Fail<bool>(new MatchError("forbidden"));
        if (match.State == MatchState.Playing || match.State == MatchState.Completed)
            return Result.Fail<bool>(new MatchError("match-in-progress"));
        if (match.State == MatchState.Abandoned)
            return Result.Ok(true);

        var now = _clock.UtcNow;
        var update = Builders<Match>.Update
            .Set(m => m.State, MatchState.Abandoned)
            .Set(m => m.UpdatedAt, now);
        // CAS on current state to avoid race with concurrent join.
        var moved = await _matches.TryAtomicUpdateAsync(matchId, match.State, update, ct);
        if (!moved) return Result.Fail<bool>(new MatchError("match-in-progress"));

        _timeoutScheduler?.Cancel(matchId);
        if (_gameFactory != null && match.GameId is Guid gid)
        {
            _gameFactory.Delete(gid);
        }
        _hub?.Publish(matchId, "match.state-changed",
            new { matchId, state = "Abandoned", transitionedAt = now });
        return Result.Ok(true);
    }

    // -----------------------------------------------------------------------
    // OnPriorityPassedAsync — decrement previous holder's clock
    // -----------------------------------------------------------------------

    public async Task OnPriorityPassedAsync(Guid matchId, string newHolderSub, CancellationToken ct)
    {
        var match = await _matches.GetByIdAsync(matchId, ct);
        if (match == null || match.State != MatchState.Playing) return;
        if (match.PriorityHolderSub == null || match.PriorityStartedAt == null) return;

        var now = _clock.UtcNow;
        var elapsed = (long)(now - match.PriorityStartedAt.Value).TotalMilliseconds;
        var prevSub = match.PriorityHolderSub;
        var stored = prevSub == match.Creator.Sub ? match.CreatorMillisRemaining
                   : prevSub == match.Opponent?.Sub ? match.OpponentMillisRemaining
                   : 0;
        var newRemaining = stored - elapsed;

        if (newRemaining <= 0)
        {
            await OnTimeoutAsync(matchId, prevSub, ct);
            return;
        }

        var update = MongoDB.Driver.Builders<Match>.Update
            .Set(prevSub == match.Creator.Sub ? m => m.CreatorMillisRemaining : m => m.OpponentMillisRemaining, newRemaining)
            .Set(m => m.PriorityHolderSub, newHolderSub)
            .Set(m => m.PriorityStartedAt, now)
            .Set(m => m.UpdatedAt, now);

        var moved = await _matches.TryAtomicUpdateAsync(matchId, MatchState.Playing, update, ct);
        if (!moved) return;

        var fresh = (await _matches.GetByIdAsync(matchId, ct))!;
        var newRemain = newHolderSub == fresh.Creator.Sub
            ? fresh.CreatorMillisRemaining
            : fresh.OpponentMillisRemaining;
        _timeoutScheduler?.Schedule(matchId, newHolderSub, newRemain);
        _hub?.Publish(matchId, "match.clock-update", new
        {
            matchId,
            creatorMs = fresh.CreatorMillisRemaining,
            opponentMs = fresh.OpponentMillisRemaining,
            holder = newHolderSub,
            startedAt = now,
        });
    }

    // -----------------------------------------------------------------------
    // OnTimeoutAsync — clock expired for a player
    // -----------------------------------------------------------------------

    public async Task OnTimeoutAsync(Guid matchId, string loserSub, CancellationToken ct)
    {
        var match = await _matches.GetByIdAsync(matchId, ct);
        if (match == null || match.State != MatchState.Playing) return;

        var winner = loserSub == match.Creator.Sub ? match.Opponent!.Sub : match.Creator.Sub;
        var now = _clock.UtcNow;
        var update = MongoDB.Driver.Builders<Match>.Update
            .Set(m => m.State, MatchState.Completed)
            .Set(m => m.WinnerSub, winner)
            .Set(m => m.TimeoutLoserSub, loserSub)
            .Set(m => m.UpdatedAt, now);
        var moved = await _matches.TryAtomicUpdateAsync(matchId, MatchState.Playing, update, ct);
        if (!moved) return;

        _timeoutScheduler?.Cancel(matchId);
        _hub?.Publish(matchId, "match.timed-out", new { matchId, loserSub, winnerSub = winner });
        _hub?.Publish(matchId, "match.state-changed",
            new { matchId, state = "Completed", transitionedAt = now });
    }

    // -----------------------------------------------------------------------
    // GetAsync
    // -----------------------------------------------------------------------

    public async Task<Result<MatchDto>> GetAsync(string callerSub, Guid matchId, CancellationToken ct)
    {
        var match = await _matches.GetByIdAsync(matchId, ct);
        if (match == null) return Result.Fail<MatchDto>(new MatchError("match-not-found"));

        // Invite matches are private: only party members may view
        if (match.Visibility == MatchVisibility.Invite)
        {
            var isParty = callerSub == match.Creator.Sub || callerSub == match.Opponent?.Sub;
            if (!isParty) return Result.Fail<MatchDto>(new MatchError("private-match"));
        }

        return Result.Ok(ToDto(match));
    }

    // -----------------------------------------------------------------------
    // ListOpenPublicAsync
    // -----------------------------------------------------------------------

    public async Task<IReadOnlyList<MatchDto>> ListOpenPublicAsync(CancellationToken ct)
    {
        var matches = await _matches.ListOpenPublicAsync(50, ct);
        return matches.Select(ToDto).ToList();
    }

    // -----------------------------------------------------------------------
    // SubmitCommandAsync
    // -----------------------------------------------------------------------

    public async Task<Result<bool>> SubmitCommandAsync(
        string callerSub, Guid matchId, GameCommand command, CancellationToken ct)
    {
        var match = await _matches.GetByIdAsync(matchId, ct);
        if (match == null) return Result.Fail<bool>(new MatchError("match-not-found"));

        var isParty = callerSub == match.Creator.Sub || callerSub == match.Opponent?.Sub;
        if (!isParty) return Result.Fail<bool>(new MatchError("forbidden"));
        if (match.State != MatchState.Playing) return Result.Fail<bool>(new MatchError("match-not-open"));
        if (match.GameId is not Guid gid) return Result.Fail<bool>(new MatchError("game-not-started"));
        if (_gameFactory == null) return Result.Fail<bool>(new MatchError("game-not-started"));

        var facade = _gameFactory.Get(gid);
        if (facade == null) return Result.Fail<bool>(new MatchError("game-not-started"));

        await facade.SubmitAsync(command, ct);
        return Result.Ok(true);
    }

    // -----------------------------------------------------------------------
    // GetGameStateAsync
    // -----------------------------------------------------------------------

    public async Task<Result<GameStateDto>> GetGameStateAsync(
        string callerSub, Guid matchId, CancellationToken ct)
    {
        var match = await _matches.GetByIdAsync(matchId, ct);
        if (match == null) return Result.Fail<GameStateDto>(new MatchError("match-not-found"));

        var isParty = callerSub == match.Creator.Sub || callerSub == match.Opponent?.Sub;
        if (!isParty) return Result.Fail<GameStateDto>(new MatchError("forbidden"));

        if (match.State != MatchState.Playing && match.State != MatchState.Completed)
            return Result.Fail<GameStateDto>(new MatchError("game-not-started"));

        if (match.GameId is not Guid gid) return Result.Fail<GameStateDto>(new MatchError("game-not-started"));
        if (_gameFactory == null) return Result.Fail<GameStateDto>(new MatchError("game-not-started"));

        var facade = _gameFactory.Get(gid);
        if (facade == null) return Result.Fail<GameStateDto>(new MatchError("game-not-started"));

        var state = facade.GetState();
        return Result.Ok(state);
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
            Creator: new MatchPlayerDto(m.Creator.Sub, m.Creator.Handle, m.Creator.DeckId, m.Creator.DeckSnapshot),
            Opponent: m.Opponent is null
                ? null
                : new MatchPlayerDto(m.Opponent.Sub, m.Opponent.Handle, m.Opponent.DeckId, m.Opponent.DeckSnapshot),
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
