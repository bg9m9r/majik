using Majik.Bot.Diagnostics;
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
    private readonly IMatchOwnership? _ownership;
    private readonly IMatchCommandForwarder? _forwarder;
    private readonly IInstanceIdProvider? _instanceIds;
    private readonly MatchFacadeBridge? _facadeBridge;
    private readonly MatchReplayBuffer? _replayBuffer;
    private readonly IBotMatchScheduler _botScheduler;
    // Slice 5a — per-(match, sub) auto-pass prefs. The engine consults
    // this on every priority window to decide whether to short-circuit
    // the human's prompt. Null = no prefs surface registered (legacy
    // callers / tests), and the loop falls back to its always-prompt
    // pre-Slice-5a behaviour.
    private readonly AutoPassPrefsStore? _autoPassPrefs;
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
        IMatchOwnership? ownership = null,
        IMatchCommandForwarder? forwarder = null,
        IInstanceIdProvider? instanceIds = null,
        ILogger<MatchService>? logger = null,
        IDeckOwnershipPolicy? deckOwnershipPolicy = null,
        MatchFacadeBridge? facadeBridge = null,
        MatchReplayBuffer? replayBuffer = null,
        IBotMatchScheduler? botScheduler = null,
        AutoPassPrefsStore? autoPassPrefs = null)
    {
        _autoPassPrefs = autoPassPrefs;
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
        _ownership = ownership;
        _forwarder = forwarder;
        _instanceIds = instanceIds;
        _facadeBridge = facadeBridge;
        _replayBuffer = replayBuffer;
        _botScheduler = botScheduler ?? NullBotMatchScheduler.Instance;
        _logger = logger;

        // Strict by default: refuse construction without a real
        // DeckRepository + DeckValidationService. The legacy stub path
        // skipped the per-owner check in ResolveDeckSnapshotAsync, which
        // let any caller quote any deck id and have the match service
        // treat it as theirs. Tests that genuinely use StubDeckLoader
        // inject AllowStubDeckOwnershipPolicy to opt back into the
        // unchecked path.
        var policy = deckOwnershipPolicy ?? new StrictDeckOwnershipPolicy();
        if ((_deckRepo == null || _deckValidator == null) && !policy.AllowMissingDeckPlumbing)
        {
            throw new InvalidOperationException(
                "MatchService requires DeckRepository and DeckValidationService " +
                "(strict deck-ownership policy). Inject AllowStubDeckOwnershipPolicy " +
                "in tests that intentionally use the stub deck loader.");
        }
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
            // Determinism (PLAN 08 prerequisite): pin + persist the RNG seed at
            // match creation so the engine's GameRandom is reproducible and a
            // future replay / rehydration can reconstruct the same (seed,
            // commands) game.
            GameSeed = NewGameSeed(),
            CreatorMillisRemaining = initialBalance,
            OpponentMillisRemaining = initialBalance,
            PriorityHolderSub = null,
            PriorityStartedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _matches.InsertAsync(match, ct);

        return Result.Ok(ToDto(match, viewerSub: callerSub));
    }

    // -----------------------------------------------------------------------
    // CreateBotMatchAsync — vs-Bot branch of CreateAsync.
    //
    // Differences from the human-vs-human path:
    //   * Opponent seat is synthesized in-process (Sub = "bot:<archetype>");
    //     no second-player join, no roll.
    //   * Visibility is forced to Invite so bot matches never surface in the
    //     public lobby listing.
    //   * State transitions follow the same path as the human flow:
    //     Open → Joined → Starting → Rolling → Playing. The bot dwells
    //     briefly in Rolling (driven by IBotMatchScheduler) so the user
    //     can see the dice roll on the frontend before play starts;
    //     transition into Playing is handled by PlayDrawAsync (either
    //     bot-triggered after it wins the roll, or human-triggered).
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

        // matchId is generated up-front so the bot-thinking callback can
        // close over it before the facade (and its embedded bot agent) are
        // constructed below.
        var matchId = Guid.NewGuid();

        // 1) Load decks + create facade BEFORE any DB write so a
        //    DeckLoadException cannot leave an orphan Match document.
        GameFacade? facade = null;
        if (_gameFactory != null)
        {
            try
            {
                var creatorDeck = await _decks.LoadAsync(creator.DeckId, ct);
                var botDeck = await _decks.LoadFromCardNamesAsync(botSnapshot, ct);
                // Bridge engine-internal bot-thinking signal to the SignalR
                // hub so the frontend can show a "Bot is thinking…"
                // indicator while the policy runs.
                var hubForCallback = _hub;
                Action<bool>? onBotThinking = hubForCallback != null
                    ? thinking => hubForCallback.PublishBotThinking(matchId, thinking)
                    : null;
                // Per-match SignalR sink for bot decision diagnostics. Gated
                // on the same flag the logger sink reads so wire + stdout
                // toggle in lockstep. The sink captures matchId at
                // construction; its lifetime is bounded by the facade —
                // when the facade is deleted (Abandon / Concede / Timeout
                // funnel through _gameFactory.Delete) the bot agent that
                // holds the reference goes with it.
                IBotDecisionSink? signalrSink = null;
                if (_hub != null && _gameFactory.BotDecisionLoggingEnabled)
                {
                    signalrSink = new SignalrBotDecisionSink(matchId, _hub);
                }

                // Per-match replay sink. Always wired when the replay
                // buffer is registered — unlike SignalrBotDecisionSink
                // it isn't gated on Bot:DecisionLogging:Enabled, because
                // the replay endpoint is meant to capture every game we
                // can. Composed with signalrSink (if present) via
                // CompositeBotDecisionSink so both observers see each
                // decision; the existing extraDecisionSink composition
                // inside ServerGameFactory.Create then folds in the
                // process-wide logger sink.
                IBotDecisionSink? replaySink = _replayBuffer != null
                    ? new ReplayBufferBotDecisionSink(matchId, _replayBuffer)
                    : null;
                var perMatchSink = Majik.Bot.Diagnostics.CompositeBotDecisionSink.Compose(signalrSink, replaySink);
                var extraSinkArg = ReferenceEquals(perMatchSink, Majik.Bot.Diagnostics.NullBotDecisionSink.Instance)
                    ? null
                    : perMatchSink;
                facade = _gameFactory.Create(
                    creator.Handle, botPlayer.Handle,
                    creatorDeck, botDeck,
                    botSeatArchetype: bot.Archetype,
                    onBotThinking: onBotThinking,
                    extraDecisionSink: extraSinkArg);
                // Wire the engine→SignalR bridge before any engine work
                // can fire events (StartFullGameAsync happens later). The
                // bridge holds the IDisposable subscriptions; teardown
                // in the catch block / terminal-state handlers calls
                // Detach.
                _facadeBridge?.Attach(matchId, creator.Sub, botPlayer.Sub, facade);
                if (_ownership != null) await _ownership.TryClaimAsync(matchId, ct);
                if (_forwarder != null) await _forwarder.OnClaimedAsync(matchId, ct);
            }
            catch (DeckLoadException ex)
            {
                // Don't surface ex.Message — it names specific cards / deck ids
                // (e.g. "unknown card at load time: <name>"), which leaks
                // internal load detail. Mirror the CardsEndpoints posture: log
                // the full message server-side, return a generic client message
                // (same code/status). (Info leak hardening.)
                _logger?.LogWarning(ex,
                    "Bot match create: deck load failed. MatchId={MatchId} CreatorSub={CreatorSub} Detail={Detail}",
                    matchId, creator.Sub, ex.Message);
                return Result.Fail<MatchDto>(new MatchError(
                    "deck-invalid", "One or more cards in the deck are invalid"));
            }
            catch (Exception ex)
            {
                // Any other exception (card-factory binder throwing, engine
                // construction failure, etc.) used to escape to the global
                // UseExceptionHandler, which returned an opaque
                // `{"error":"internal"}`. Surface the exception type + message
                // here so the portal can show e.g.
                // `internal: NullReferenceException: …` and the user has a
                // breadcrumb for which card crashed the factory. Full stack
                // trace stays in the server log via ILogger; the wire body
                // intentionally omits it to avoid leaking internals.
                _logger?.LogError(ex,
                    "Bot match create failed during deck-load/facade-create. " +
                    "MatchId={MatchId} CreatorSub={CreatorSub} Archetype={Archetype}",
                    matchId, creator.Sub, bot.Archetype);

                // Best-effort cleanup of any partial facade/ownership state
                // before we bail. The Match doc has not been inserted yet at
                // this point, so we only need to unwind the facade + bridge
                // + ownership claim if they got created.
                if (_gameFactory != null && facade != null)
                {
                    try
                    {
                        _facadeBridge?.Detach(matchId);
                        _gameFactory.Delete(facade.GameId);
                        if (_ownership != null) await _ownership.ReleaseAsync(matchId, ct);
                        if (_forwarder != null) await _forwarder.OnReleasedAsync(matchId, ct);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger?.LogError(cleanupEx,
                            "Cleanup after bot-match facade failure also threw. " +
                            "MatchId={MatchId}", matchId);
                    }
                }

                return Result.Fail<MatchDto>(new MatchError(
                    "internal",
                    $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

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
            // Determinism (PLAN 08 prerequisite): pin + persist the RNG seed at
            // match creation (see human-vs-human path above).
            GameSeed = NewGameSeed(),
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

            // Bot matches follow the same lifecycle as human-vs-human:
            // Open → Joined → Starting → Rolling. Transition into
            // Playing is deferred until PlayDrawAsync lands (either
            // human's choice if they won the roll, or the bot scheduler's
            // greedy "play" pick if the bot won).
            if (!await TryTransitionStateAsync(matchId, MatchState.Open, MatchState.Joined, now, ct))
                throw new InvalidOperationException("CAS conflict during bot match setup (Open→Joined).");
            if (!await TryTransitionStateAsync(matchId, MatchState.Joined, MatchState.Starting, now, ct))
                throw new InvalidOperationException("CAS conflict during bot match setup (Joined→Starting).");
            if (!await TryTransitionStateAsync(matchId, MatchState.Starting, MatchState.Rolling, now, ct))
                throw new InvalidOperationException("CAS conflict during bot match setup (Starting→Rolling).");

            // Initialize empty roll record so SubmitRollAsync can update it
            // in place (mirrors the JoinAsync flow).
            var setRoll = Builders<Match>.Update
                .Set(m => m.Roll, new MatchRoll())
                .Set(m => m.UpdatedAt, _clock.UtcNow);
            await _matches.TryAtomicUpdateAsync(matchId, MatchState.Rolling, setRoll, ct);

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
                try
                {
                    // Detach the bridge BEFORE deleting the facade so we
                    // don't leak hub subscriptions on a partial-setup
                    // rollback.
                    _facadeBridge?.Detach(matchId);
                    _gameFactory.Delete(facade.GameId);
                    if (_ownership != null) await _ownership.ReleaseAsync(matchId, ct);
                    if (_forwarder != null) await _forwarder.OnReleasedAsync(matchId, ct);
                }
                catch (Exception facEx)
                {
                    _logger?.LogError(facEx,
                        "Failed to dispose facade during bot-match setup cleanup. GameId={GameId}",
                        facade.GameId);
                }
            }
            _logger?.LogError(ex,
                "Bot match setup failed; rolled back. MatchId={MatchId}", matchId);
            return Result.Fail<MatchDto>(new MatchError(
                "internal",
                $"Bot match setup failed: {ex.GetType().Name}: {ex.Message}"));
        }

        // 3) Engine startup is deferred to PlayDrawAsync (same as the
        //    human-vs-human flow). We're still in Rolling state here —
        //    StartFullGameAsync fires once the play/draw choice lands.

        // 4) Schedule the bot to submit its dice roll after a brief dwell.
        //    The bot uses the same SubmitRollAsync path as a human player,
        //    so the SignalR `match.player-rolled` + `match.rolled` events
        //    fire identically and the frontend's RollingStateComponent can
        //    render the dice. If the bot wins, SubmitRollAsync will in turn
        //    schedule its PlayDraw follow-up; if the human wins, the match
        //    sits in Rolling until the human posts /play-draw.
        _botScheduler.ScheduleBotRoll(matchId, botPlayer.Sub);

        // Re-fetch so the returned DTO reflects any state mutations the
        // bot scheduler made synchronously (test path with
        // SynchronousBotMatchScheduler — production fires-and-forgets
        // and this re-read is a noop). Without this the wire payload
        // returned to the test client would never include the bot's
        // roll, even after the scheduler had submitted it.
        fresh = (await _matches.GetByIdAsync(matchId, ct)) ?? fresh;
        return Result.Ok(ToDto(fresh, viewerSub: creator.Sub));
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
                // Wire the engine→SignalR bridge as soon as the facade
                // exists. Subsequent state transitions (Joined→Starting→
                // Rolling) only fire match.* publisher events, but the
                // engine itself can start emitting EventDtos the moment
                // PlayDrawAsync calls StartFullGameAsync — the bridge
                // must already be attached by then.
                _facadeBridge?.Attach(matchId, match.Creator.Sub, opponent.Sub, facade);
                await _matches.TryAtomicUpdateAsync(matchId, MatchState.Joined,
                    Builders<Match>.Update.Set(m => m.GameId, facade.GameId),
                    ct);
                if (_ownership != null) await _ownership.TryClaimAsync(matchId, ct);
                if (_forwarder != null) await _forwarder.OnClaimedAsync(matchId, ct);
            }
            catch (DeckLoadException ex)
            {
                // Don't surface ex.Message — it names specific cards / deck ids,
                // leaking internal load detail. Log full detail server-side,
                // return a generic client message (same code/status). Mirrors
                // the CardsEndpoints "don't surface ex.Message" posture.
                _logger?.LogWarning(ex,
                    "Join match: deck load failed. MatchId={MatchId} CallerSub={CallerSub} Detail={Detail}",
                    matchId, callerSub, ex.Message);
                return Result.Fail<MatchDto>(new MatchError(
                    "deck-invalid", "One or more cards in the deck are invalid"));
            }
            catch (Exception ex)
            {
                // Mirror the bot-match branch: surface the exception type +
                // message so a card-factory or engine-construction failure
                // doesn't silently fall through to the global handler and
                // come back as opaque `{"error":"internal"}`. Stack stays
                // in the log only.
                _logger?.LogError(ex,
                    "Join match failed during deck-load/facade-create. " +
                    "MatchId={MatchId} CallerSub={CallerSub}",
                    matchId, callerSub);
                return Result.Fail<MatchDto>(new MatchError(
                    "internal",
                    $"{ex.GetType().Name}: {ex.Message}"));
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
        return Result.Ok(ToDto(fresh, viewerSub: callerSub));
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
        if (PlayDrawPreconditionError(match, callerSub) is { } preErr)
            return Result.Fail<MatchDto>(preErr);

        var choice = request.Choice?.ToLowerInvariant();
        if (choice != "play" && choice != "draw")
            return Result.Fail<MatchDto>(new MatchError("invalid-choice"));

        var firstPlayerSub = ResolveFirstPlayerSub(match!, callerSub, choice);
        var firstPlayerSlot = firstPlayerSub == match!.Creator.Sub ? 0 : 1;

        var now = _clock.UtcNow;
        var update = Builders<Match>.Update
            .Set(m => m.FirstChoice, choice)
            .Set(m => m.State, MatchState.Playing)
            .Set(m => m.PriorityHolderSub, firstPlayerSub)
            .Set(m => m.PriorityStartedAt, now)
            .Set(m => m.UpdatedAt, now);

        var moved = await _matches.TryAtomicUpdateAsync(matchId, MatchState.Rolling, update, ct);
        if (!moved) return Result.Fail<MatchDto>(new MatchError("not-rolling"));

        StartGameForFirstPlayer(match, firstPlayerSlot);
        PublishPlayDrawTransitionEvents(match, choice!, firstPlayerSub, now);

        var fresh = (await _matches.GetByIdAsync(matchId, ct))!;
        return Result.Ok(ToDto(fresh, viewerSub: callerSub));
    }

    /// <summary>PlayDraw entry-gate checks: match exists, state is Rolling,
    /// and the caller actually won the roll. Returns a structured error or
    /// null when all preconditions hold.</summary>
    private static MatchError? PlayDrawPreconditionError(Match? match, string callerSub)
    {
        if (match == null) return new MatchError("match-not-found");
        if (match.State != MatchState.Rolling) return new MatchError("not-rolling");
        if (match.Roll == null || match.Roll.WinnerSub != callerSub)
            return new MatchError("not-roll-winner");
        return null;
    }

    /// <summary>Compute the sub of whoever takes the first turn. "play" puts
    /// the caller first; "draw" hands first turn to the opponent.</summary>
    private static string ResolveFirstPlayerSub(Match match, string callerSub, string choice)
        => choice == "play"
            ? callerSub
            : callerSub == match.Creator.Sub ? match.Opponent!.Sub : match.Creator.Sub;

    /// <summary>Determinism (PLAN 08 prerequisite): mint a fresh RNG seed for a
    /// new match. Stored on the Match doc and used to build the engine's
    /// <c>GameRandom(seed)</c> so the game is reproducible given (seed,
    /// commands).</summary>
    private static int NewGameSeed() => System.Random.Shared.Next();

    /// <summary>Boot the engine for the just-decided first-player slot and
    /// wire the Slice 5a auto-pass prefs provider so the engine can short-
    /// circuit the human's priority prompt when prefs match.</summary>
    private void StartGameForFirstPlayer(Match match, int firstPlayerSlot)
    {
        if (_gameFactory == null || match.GameId is not Guid gid) return;
        var facade = _gameFactory.Get(gid);
        var prefsProvider = BuildAutoPassPrefsProvider(match, facade);
        // Determinism (PLAN 08 prerequisite): build the engine RNG from the
        // seed pinned + persisted at match creation, instead of letting the
        // driver mint a fresh, unpersisted GameRandom. This is what makes the
        // game reproducible from (stored seed, command log) for later replay /
        // rehydration (the rehydration body itself remains out of scope).
        var rng = new Majik.Core.Random.GameRandom(match.GameSeed);
        facade?.StartFullGameAsync(firstPlayerSlot, rng: rng, autoPassPrefsProvider: prefsProvider);
    }

    /// <summary>Slice 5a — build the per-seat AutoPassPrefs provider. The
    /// provider maps the engine's Player (Alice/Bob) back to the
    /// corresponding match sub, then to the store. Bot seats (sub starts
    /// with "bot:") return null so the BotPlayerAgent's own
    /// <c>ChoosePriorityActionAsync</c> is never short-circuited; the store
    /// also fully guards via <c>Has()</c> — a sub for which no entry has
    /// ever been written returns null. Captures <paramref name="match"/>.Id
    /// so the lambda is self-contained; the facade outlives this call by
    /// the duration of the game.</summary>
    private Func<Majik.Core.Players.Player, Majik.Core.Game.IAutoPassPrefsView?>?
        BuildAutoPassPrefsProvider(Match match, Majik.Core.Api.GameFacade? facade)
    {
        if (_autoPassPrefs == null || facade == null) return null;
        var prefsMatchId = match.Id;
        var creatorSub = match.Creator.Sub;
        var opponentSub = match.Opponent?.Sub;
        var aliceId = facade.Alice.Id;
        var prefsStore = _autoPassPrefs;
        return player =>
        {
            var sub = player.Id == aliceId ? creatorSub : opponentSub;
            if (sub == null || sub.StartsWith("bot:", StringComparison.Ordinal))
            {
                return null;
            }
            // Opt-in semantics: auto-pass is DISABLED until the portal has
            // explicitly PUT a prefs snapshot for this (matchId, sub).
            if (!prefsStore.Has(prefsMatchId, sub)) return null;
            return prefsStore.Get(prefsMatchId, sub);
        };
    }

    /// <summary>Fan out the post-PlayDraw events (timeout schedule + SignalR
    /// hub publishes for play-draw-chosen / state-changed / clock-update).</summary>
    private void PublishPlayDrawTransitionEvents(
        Match match, string choice, string firstPlayerSub, DateTime now)
    {
        _timeoutScheduler?.Schedule(match.Id, firstPlayerSub, match.ClockMinutes * 60_000L);
        _hub?.Publish(match.Id, "match.play-draw-chosen",
            new { matchId = match.Id, choice, firstPlayerSub });
        _hub?.Publish(match.Id, "match.state-changed",
            new { matchId = match.Id, state = "Playing", transitionedAt = now });
        _hub?.Publish(match.Id, "match.clock-update",
            new
            {
                matchId = match.Id,
                creatorMs = match.CreatorMillisRemaining,
                opponentMs = match.OpponentMillisRemaining,
                holder = firstPlayerSub,
                startedAt = now,
            });
    }

    // -----------------------------------------------------------------------
    // SubmitRollAsync
    // -----------------------------------------------------------------------

    public async Task<Result<MatchDto>> SubmitRollAsync(string callerSub, Guid matchId, CancellationToken ct)
    {
        var match = await _matches.GetByIdAsync(matchId, ct);
        if (SubmitRollPreconditionError(match, callerSub) is { } preErr)
            return Result.Fail<MatchDto>(preErr);

        var isCreator = callerSub == match!.Creator.Sub;
        var existing = match.Roll ?? new MatchRoll();

        // Idempotent: if caller's slot already set, just return current snapshot.
        var callerSlotFilled = isCreator ? existing.CreatorRoll.HasValue : existing.OpponentRoll.HasValue;
        if (callerSlotFilled)
        {
            return Result.Ok(ToDto(match, viewerSub: callerSub));
        }

        var seatOutcome = await TryWriteCallerSeatRollAsync(matchId, callerSub, isCreator, ct);
        if (seatOutcome.EarlyReturn is { } early) return early;

        // Re-read to see whether BOTH slots are now filled.
        var afterSet = await _matches.GetByIdAsync(matchId, ct);
        await TryResolveRollWinnerAsync(matchId, afterSet, ct);

        var fresh = (await _matches.GetByIdAsync(matchId, ct))!;
        return Result.Ok(ToDto(fresh, viewerSub: callerSub));
    }

    private readonly record struct SeatRollOutcome(Result<MatchDto>? EarlyReturn);

    /// <summary>SubmitRoll entry-gate checks: match exists, state is Rolling,
    /// and caller is one of the two seats.</summary>
    private static MatchError? SubmitRollPreconditionError(Match? match, string callerSub)
    {
        if (match == null) return new MatchError("match-not-found");
        if (match.State != MatchState.Rolling) return new MatchError("not-rolling");
        var isCreator = callerSub == match.Creator.Sub;
        var isOpponent = match.Opponent != null && callerSub == match.Opponent.Sub;
        if (!isCreator && !isOpponent) return new MatchError("not-a-player");
        return null;
    }

    /// <summary>Generate the caller's per-seat roll and persist it via a
    /// field-targeted CAS guarded on "slot was empty". The CAS closes the
    /// lost-update window (two concurrent submissions used to read the same
    /// Roll, mutate in-process, and clobber the other seat — Slice 4a #5)
    /// and makes racing same-seat double-submits idempotent. On CAS miss the
    /// current snapshot is returned (also idempotent).</summary>
    private async Task<SeatRollOutcome> TryWriteCallerSeatRollAsync(
        Guid matchId, string callerSub, bool isCreator, CancellationToken ct)
    {
        var value = _dice.RollSingle();
        var now = _clock.UtcNow;
        var slotSet = await _matches.TrySetPlayerRollAsync(matchId, isCreator, value, now, ct);
        if (!slotSet)
        {
            var snapshot = await _matches.GetByIdAsync(matchId, ct);
            if (snapshot == null)
                return new SeatRollOutcome(Result.Fail<MatchDto>(new MatchError("match-not-found")));
            if (snapshot.State != MatchState.Rolling)
                return new SeatRollOutcome(Result.Fail<MatchDto>(new MatchError("not-rolling")));
            return new SeatRollOutcome(Result.Ok(ToDto(snapshot, viewerSub: callerSub)));
        }

        _hub?.Publish(matchId, "match.player-rolled", new { matchId, sub = callerSub, roll = value });
        return new SeatRollOutcome(null);
    }

    /// <summary>When both slots are now filled and no winner has been
    /// stamped, attempt the winner CAS. Exactly one concurrent caller wins
    /// it (WinnerSub == null guard), so the winner is computed once even
    /// under simultaneous submissions. Auto-rerolls ties (CR 104.1).</summary>
    private async Task TryResolveRollWinnerAsync(Guid matchId, Match? afterSet, CancellationToken ct)
    {
        const int MaxTieRetries = 100;
        if (afterSet?.Roll is not { CreatorRoll: not null, OpponentRoll: not null, WinnerSub: null })
            return;

        int c = afterSet.Roll.CreatorRoll!.Value;
        int o = afterSet.Roll.OpponentRoll!.Value;

        int retries = 0;
        while (c == o)
        {
            if (++retries > MaxTieRetries)
                throw new InvalidOperationException("Tie reroll cap exceeded — random source likely broken.");
            c = _dice.RollSingle();
            o = _dice.RollSingle();
        }
        var winnerSub = c > o ? afterSet.Creator.Sub : afterSet.Opponent!.Sub;

        var wonWinnerCas = await _matches.TrySetRollWinnerAsync(
            matchId, c, o, winnerSub, _clock.UtcNow, ct);
        if (!wonWinnerCas) return;

        _hub?.Publish(matchId, "match.rolled",
            new { matchId, roll = new MatchRollDto(c, o, winnerSub) });

        // Bot seats stamp "bot:<archetype>" in CreateBotMatchAsync. Gating
        // this on the winner CAS means PlayDraw is scheduled exactly once
        // even under racing submissions (Slice 4a #7).
        if (winnerSub.StartsWith("bot:", StringComparison.Ordinal))
        {
            _botScheduler.ScheduleBotPlayDraw(matchId, winnerSub);
        }
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

        // Cleanup runs REGARDLESS of the CAS result (Slice 4a #8). Both
        // operations are idempotent, and a CAS conflict here means a
        // concurrent timeout already moved the match to Completed — but THIS
        // replica still holds the timer + bridge subscriptions for the match
        // and would leak them if we returned without tearing them down. The
        // winning timeout path detaches its own copy; on a single replica
        // both reference the same instances, so the double-detach is a no-op.
        _timeoutScheduler?.Cancel(matchId);
        // Tear down engine→SignalR bridge: match is over, no further
        // EventDto / PromptDto traffic should reach the hub group.
        _facadeBridge?.Detach(matchId);
        // Slice 5a — terminal state, evict per-(matchId, *) auto-pass
        // prefs so the store doesn't grow unbounded across the server's
        // lifetime. Idempotent on a no-match-found.
        _autoPassPrefs?.EvictMatch(matchId);

        if (!moved) return Result.Fail<MatchDto>(new MatchError("cannot-concede"));

        _hub?.Publish(matchId, "match.state-changed",
            new { matchId, state = "Completed", transitionedAt = now });

        var fresh = (await _matches.GetByIdAsync(matchId, ct))!;
        return Result.Ok(ToDto(fresh, viewerSub: callerSub));
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
        // Tear down engine→SignalR bridge BEFORE deleting the facade so
        // any in-flight EventDto can't dereference a disposed engine.
        _facadeBridge?.Detach(matchId);
        if (_gameFactory != null && match.GameId is Guid gid)
        {
            _gameFactory.Delete(gid);
        }
        if (_ownership != null) await _ownership.ReleaseAsync(matchId, ct);
        if (_forwarder != null) await _forwarder.OnReleasedAsync(matchId, ct);
        // Slice 5a — abandon is terminal; evict prefs (matches the
        // Concede / Timeout posture so the store sheds the same set of
        // entries regardless of which terminal path was taken).
        _autoPassPrefs?.EvictMatch(matchId);
        _hub?.Publish(matchId, "match.state-changed",
            new { matchId, state = "Abandoned", transitionedAt = now });
        return Result.Ok(true);
    }

    // -----------------------------------------------------------------------
    // OnPriorityPassedAsync — decrement previous holder's clock
    // -----------------------------------------------------------------------

    /// <param name="expectedPrevHolderSub">
    /// The prior priority-holder sub the caller observed when it decided to
    /// hand off. Threaded through as a compare-and-swap guard: if the stored
    /// holder no longer equals this value (a concurrent / out-of-order handoff
    /// already moved it — e.g. A→B then B→A on fast turn cycling), the update
    /// no-ops so the same elapsed slice can't be billed twice or a transition
    /// dropped. Pass null to opt out of the CAS (legacy callers / direct
    /// tests); the method then falls back to the stored holder as before.
    /// </param>
    public async Task OnPriorityPassedAsync(
        Guid matchId, string newHolderSub, CancellationToken ct, string? expectedPrevHolderSub = null)
    {
        var match = await _matches.GetByIdAsync(matchId, ct);
        if (match == null || match.State != MatchState.Playing) return;
        if (match.PriorityHolderSub == null || match.PriorityStartedAt == null) return;

        var prevSub = match.PriorityHolderSub;

        // CAS guard: if the caller named the holder it expected to displace
        // and the engine has since moved past it, this handoff is stale —
        // drop it rather than re-bill the already-rotated holder (C1). The
        // authoritative re-check still happens in the atomic update below;
        // this early-out just saves a wasted round-trip on the common miss.
        if (expectedPrevHolderSub != null && prevSub != expectedPrevHolderSub)
        {
            _logger?.LogDebug(
                "OnPriorityPassedAsync: stale handoff ignored. MatchId={MatchId} " +
                "ExpectedPrev={ExpectedPrev} StoredHolder={StoredHolder} NewHolder={NewHolder}",
                matchId, expectedPrevHolderSub, prevSub, newHolderSub);
            return;
        }

        var now = _clock.UtcNow;
        // Clock skew / a stale PriorityStartedAt slightly in the future would
        // make elapsed negative and CREDIT the holder time. Clamp elapsed to
        // ≥0, then clamp the resulting balance to ≥0 so a negative balance can
        // never be persisted (Slice 4a #4).
        var elapsed = Math.Max(0, (long)(now - match.PriorityStartedAt.Value).TotalMilliseconds);
        var stored = prevSub == match.Creator.Sub ? match.CreatorMillisRemaining
                   : prevSub == match.Opponent?.Sub ? match.OpponentMillisRemaining
                   : 0;
        var newRemaining = Math.Max(0, stored - elapsed);

        if (newRemaining <= 0)
        {
            await OnTimeoutAsync(matchId, prevSub, ct);
            return;
        }

        var expectedStartedAt = match.PriorityStartedAt.Value;
        var update = MongoDB.Driver.Builders<Match>.Update
            .Set(prevSub == match.Creator.Sub ? m => m.CreatorMillisRemaining : m => m.OpponentMillisRemaining, newRemaining)
            .Set(m => m.PriorityHolderSub, newHolderSub)
            .Set(m => m.PriorityStartedAt, now)
            .Set(m => m.UpdatedAt, now);

        // Atomic compare-and-swap on the prior holder AND the priority-start
        // timestamp (NOT just Id+State). Two rapid handoffs that both read the
        // same prior holder will serialize: the first flips PriorityHolderSub
        // + advances PriorityStartedAt, the second's filter (holder==prevSub
        // AND startedAt==expectedStartedAt) no longer matches and it no-ops —
        // no double-bill, no dropped transition (C1 + Slice 4a #6). Adding the
        // timestamp closes the duplicate/late-handoff window where holder is
        // unchanged (e.g. a re-fired handoff for the SAME active player) so it
        // can't deduct twice off one slice.
        //
        // Retry-with-backoff: a transient Mongo fault here would freeze the
        // clock holder, burning the wrong player's clock. The CAS gating
        // makes the retry safe — a lost-race retry finds the holder/startedAt
        // already advanced and no-ops.
        var moved = await RetryPolicy.ExecuteAsync(
            c => _matches.TryAtomicUpdateWithHolderAsync(
                matchId, MatchState.Playing, prevSub, update, c,
                constrainStartedAt: true, expectedPriorityStartedAt: expectedStartedAt),
            _logger, $"OnPriorityPassedAsync CAS (match {matchId})", ct);
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

        // Retry-with-backoff: this CAS is the ONLY thing moving the match out
        // of Playing on a clock expiry. A single transient Mongo blip here
        // would strand the match in Playing forever (the timer already fired
        // and won't fire again). The CAS filter (State==Playing) makes the
        // retry idempotent: a lost-race retry that finds the match already
        // Completed matches nothing and returns false — never a double-apply.
        var moved = await RetryPolicy.ExecuteAsync(
            c => _matches.TryAtomicUpdateAsync(matchId, MatchState.Playing, update, c),
            _logger, $"OnTimeoutAsync CAS (match {matchId})", ct);
        if (!moved) return;

        _timeoutScheduler?.Cancel(matchId);
        // Match terminated by clock — no further engine traffic should
        // surface on the hub. (Facade itself is left to MatchCleanup /
        // a future explicit teardown; the bridge just unhooks.)
        _facadeBridge?.Detach(matchId);
        // Slice 5a — timeout is terminal; evict per-match prefs.
        _autoPassPrefs?.EvictMatch(matchId);
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

        return Result.Ok(ToDto(match, viewerSub: callerSub));
    }

    // -----------------------------------------------------------------------
    // ListOpenPublicAsync
    // -----------------------------------------------------------------------

    public async Task<IReadOnlyList<MatchDto>> ListOpenPublicAsync(CancellationToken ct)
    {
        var matches = await _matches.ListOpenPublicAsync(50, ct);
        // Lobby listings strip every DeckSnapshot: a creator's full
        // decklist is the creator's own data and was never meant to be
        // visible to lobby browsers (which are arbitrary authed users).
        return matches.Select(m => ToDto(m, viewerSub: null)).ToList();
    }

    // -----------------------------------------------------------------------
    // SetAutoPassPrefsAsync — Slice 5a
    // -----------------------------------------------------------------------

    /// <summary>
    /// Slice 5a — record the caller's auto-pass preferences for this
    /// match. The next priority-window evaluation reads the updated
    /// value (no in-flight prompt is mutated — a prompt already
    /// outstanding completes on the caller's own submit).
    ///
    /// <para>Authz: caller must be party to the match (Creator or
    /// Opponent sub matches the authed sub). Mirrors
    /// <see cref="GetGameStateAsync"/>'s party check.</para>
    ///
    /// <para>404 when match doesn't exist; 403 (<c>forbidden</c>) when
    /// caller isn't party. 400 (<c>invalid-request</c>) when the body
    /// is null or PhaseStops is null (only FullControl + a non-null
    /// dict are accepted shapes — empty dict = no stops).</para>
    /// </summary>
    public async Task<Result<bool>> SetAutoPassPrefsAsync(
        string callerSub, Guid matchId, AutoPassPrefs prefs, CancellationToken ct)
    {
        if (prefs == null)
            return Result.Fail<bool>(new MatchError("invalid-request", "prefs body is required"));
        if (prefs.PhaseStops == null)
            return Result.Fail<bool>(new MatchError("invalid-request", "PhaseStops is required (empty dict allowed)"));

        var match = await _matches.GetByIdAsync(matchId, ct);
        if (match == null) return Result.Fail<bool>(new MatchError("match-not-found"));

        var isParty = callerSub == match.Creator.Sub || callerSub == match.Opponent?.Sub;
        if (!isParty) return Result.Fail<bool>(new MatchError("forbidden"));

        // Bot seats can't have prefs — defensive guard. (The endpoint
        // only authzs human subs via JWT, so this should never trigger,
        // but the explicit reject documents the intent.)
        if (callerSub.StartsWith("bot:", StringComparison.Ordinal))
            return Result.Fail<bool>(new MatchError("forbidden"));

        // Null prefs store = legacy boot, swallow the write silently
        // (the endpoint still 204s, since this is a "best-effort" UX
        // hint — the engine just falls back to its always-prompt path).
        _autoPassPrefs?.Set(matchId, callerSub, prefs);
        return Result.Ok(true);
    }

    // -----------------------------------------------------------------------
    // SubmitCommandAsync
    // -----------------------------------------------------------------------

    public async Task<Result<bool>> SubmitCommandAsync(
        string callerSub, Guid matchId, GameCommand command, CancellationToken ct)
    {
        var match = await _matches.GetByIdAsync(matchId, ct);
        if (SubmitCommandPreconditionError(match, callerSub, out var gid) is { } preErr)
            return Result.Fail<bool>(preErr);

        // Input-bounds guard (DoS). Reject pathologically large X / list
        // payloads BEFORE the command reaches the engine — see
        // CommandValidator for the caps and rationale.
        if (CommandValidator.Validate(command) is { } boundsError)
            return Result.Fail<bool>(boundsError);

        // Fast path: this replica owns the facade in-process.
        var facade = _gameFactory!.Get(gid);
        if (facade != null)
        {
            return await ExecuteCommandOnLocalFacadeAsync(match!, facade, command, callerSub, matchId, gid, ct);
        }

        // Cross-replica fallback: another replica owns the facade.
        if (await TryForwardCommandToRemoteOwnerAsync(matchId, callerSub, command, ct))
            return Result.Ok(true);

        return Result.Fail<bool>(new MatchError("game-not-started"));
    }

    /// <summary>Entry-gate checks for SubmitCommand: match exists, caller is
    /// a party, state is Playing, the engine game id is set, and the
    /// gameFactory is wired. Emits the GameId via the out param when valid.</summary>
    private MatchError? SubmitCommandPreconditionError(Match? match, string callerSub, out Guid gid)
    {
        gid = default;
        if (match == null) return new MatchError("match-not-found");
        var isParty = callerSub == match.Creator.Sub || callerSub == match.Opponent?.Sub;
        if (!isParty) return new MatchError("forbidden");
        if (match.State != MatchState.Playing) return new MatchError("match-not-open");
        if (match.GameId is not Guid g) return new MatchError("game-not-started");
        if (_gameFactory == null) return new MatchError("game-not-started");
        gid = g;
        return null;
    }

    /// <summary>Local-replica command execution. Stamps PlayerId from the
    /// caller's seat mapping (Creator→Alice, Opponent→Bob), submits to the
    /// facade, translates engine throws into "invalid-command", and ACKs
    /// any buffered prompt so a late rejoin doesn't replay a stale one.</summary>
    private async Task<Result<bool>> ExecuteCommandOnLocalFacadeAsync(
        Match match,
        Majik.Core.Api.GameFacade facade,
        GameCommand command,
        string callerSub,
        Guid matchId,
        Guid gid,
        CancellationToken ct)
    {
        // Stamping prevents seat-impersonation: even if a malicious client
        // sets PlayerId to the opponent's Guid, we overwrite it with the
        // seat derived from the authed sub.
        var seatId = callerSub == match.Creator.Sub ? facade.Alice.Id : facade.Bob.Id;
        command = command with { PlayerId = seatId };

        try
        {
            await facade.SubmitAsync(command, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller disconnect / shutdown — propagate, not a client error.
            throw;
        }
        catch (Exception ex)
        {
            // Engine throws on illegal command — log server-side and surface
            // a clean 4xx with no type / message detail (info-leak hardening).
            _logger?.LogWarning(ex,
                "Engine rejected submitted command. " +
                "MatchId={MatchId} GameId={GameId} CallerSub={CallerSub} CommandType={CommandType}",
                matchId, gid, callerSub, command.GetType().Name);
            return Result.Fail<bool>(new MatchError(
                "invalid-command",
                "The command was not valid for the current game state."));
        }
        // ACK the buffered prompt so a late JoinMatch doesn't replay it.
        _facadeBridge?.AckPrompt(matchId, callerSub);
        return Result.Ok(true);
    }

    /// <summary>Cross-replica fallback: look up ownership in Redis and
    /// forward via pub/sub. Best-effort: a stale own-instance claim will
    /// just time out and fall back to the caller-retry path.</summary>
    private async Task<bool> TryForwardCommandToRemoteOwnerAsync(
        Guid matchId, string callerSub, GameCommand command, CancellationToken ct)
    {
        if (_ownership == null || _forwarder == null || _instanceIds == null) return false;
        var owner = await _ownership.GetOwnerAsync(matchId, ct);
        if (owner == null || owner == _instanceIds.Value) return false;

        var delivered = await _forwarder.SendAsync(matchId, callerSub, command, ct);
        if (delivered) return true;
        _logger?.LogWarning(
            "Forwarded command to remote owner failed/timed-out. MatchId={MatchId} Owner={Owner}",
            matchId, owner);
        return false;
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
        if (facade == null)
        {
            // Facade entirely missing on this replica. Legitimately
            // game-not-started from this node's perspective — could be a
            // cross-replica request where the owner is another instance,
            // or the engine simply hasn't been booted yet. Log so the
            // bot-match "No game state." regression (PR #168 follow-up)
            // is observable in prod instead of silently dead.
            _logger?.LogWarning(
                "GetGameStateAsync: facade missing on this replica. MatchId={MatchId} GameId={GameId} CallerSub={CallerSub}",
                matchId, gid, callerSub);
            return Result.Fail<GameStateDto>(new MatchError("game-not-started"));
        }

        // CR 706 — return the per-viewer snapshot so the opponent's hand
        // is masked (each card surfaces as a "(hidden)" placeholder, count
        // preserved so the UI can render face-down cards). Seat mapping
        // matches the convention used everywhere else in this service:
        // Creator → Alice, Opponent → Bob. See MatchFacadeBridge.Attach
        // and ChoosePlayDrawAsync's firstPlayerSlot computation.
        var viewerPlayerId = callerSub == match.Creator.Sub
            ? facade.Alice.Id
            : facade.Bob.Id;
        var state = facade.GetStateFor(viewerPlayerId);
        return ResolveViewerStateResult(
            state, matchId, gid, callerSub, viewerPlayerId,
            facade.Alice.Id, facade.Bob.Id, isCreator: callerSub == match.Creator.Sub);
    }

    /// <summary>
    /// Decide what <see cref="GetGameStateAsync"/> returns given the
    /// per-viewer snapshot <paramref name="state"/> produced by
    /// <see cref="GameFacade.GetStateFor"/>. Extracted as a pure helper so
    /// the CR-706 hand-leak guard (Slice 4b #4) is unit-testable without a
    /// fakeable sealed <see cref="GameFacade"/>:
    /// <list type="bullet">
    ///   <item>non-null snapshot → <c>Ok(state)</c> (the masked per-viewer
    ///         view);</item>
    ///   <item>null snapshot → REFUSE. The prior behaviour fell back to
    ///         <c>facade.GetState()</c> (full-reveal spectator view), which
    ///         LEAKS the opponent's hand + library card names. We instead
    ///         emit CRITICAL + bump a counter and return a structured
    ///         <c>game-state-unavailable</c> error. The hidden zones are
    ///         NEVER serialized on this path — <c>GetState()</c> is never
    ///         called here.</item>
    /// </list>
    /// </summary>
    internal Result<GameStateDto> ResolveViewerStateResult(
        GameStateDto? state, Guid matchId, Guid gameId, string callerSub,
        Guid viewerPlayerId, Guid aliceId, Guid bobId, bool isCreator)
    {
        // CR 706 — the per-viewer snapshot masks the opponent's hand (each
        // card surfaces as a "(hidden)" placeholder, count preserved).
        if (state != null) return Result.Ok(state);

        // GetStateFor returned null — theoretically unreachable (the viewer
        // id was derived from facade.Alice.Id / facade.Bob.Id, both stable),
        // but the bot-match flow showed this branch firing in prod. We
        // REFUSE the leaky full-reveal fallback and return an error instead.
        Interlocked.Increment(ref _stateFallbackRefusedCount);
        _logger?.LogCritical(
            "GetGameStateAsync: GetStateFor returned null; REFUSING the full-reveal " +
            "fallback (would leak opponent hidden zones, CR 706). Returning " +
            "game-state-unavailable. MatchId={MatchId} GameId={GameId} CallerSub={CallerSub} " +
            "ViewerPlayerId={ViewerPlayerId} AliceId={AliceId} BobId={BobId} " +
            "IsCreator={IsCreator}",
            matchId, gameId, callerSub,
            viewerPlayerId, aliceId, bobId, isCreator);
        return Result.Fail<GameStateDto>(new MatchError("game-state-unavailable",
            "Per-viewer game state could not be produced; refusing to serve a full-reveal view."));
    }

    /// <summary>Visible for tests / metrics — number of times
    /// <see cref="GetGameStateAsync"/> refused the leaky full-reveal
    /// fallback (CR 706 hand-leak guard, Slice 4b #4). Should be 0 in a
    /// healthy system; a non-zero value flags the null-snapshot regression.</summary>
    internal long StateFallbackRefusedCount => Interlocked.Read(ref _stateFallbackRefusedCount);
    private long _stateFallbackRefusedCount;

    // -----------------------------------------------------------------------
    // GetReplayAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the in-memory replay buffer for <paramref name="matchId"/>.
    /// Access is restricted to seated players (invite matches are private;
    /// see <see cref="GetAsync"/> for the same authorization pattern). The
    /// replay endpoint is "share a finished game" — the caller is expected
    /// to be a party to that game.
    ///
    /// <para>Returns <c>match-not-found</c> when no match document exists,
    /// <c>forbidden</c> when the caller isn't a seated player, and
    /// <c>match-not-found</c> when the buffer has been evicted (LRU under
    /// <see cref="MatchReplayBuffer.MaxRetainedMatches"/>) — the buffer
    /// loss is indistinguishable from the match not existing for a
    /// downloader that just wants the JSON.</para>
    /// </summary>
    public async Task<Result<MatchReplayDto>> GetReplayAsync(
        string callerSub, Guid matchId, CancellationToken ct)
    {
        if (_replayBuffer == null)
            return Result.Fail<MatchReplayDto>(new MatchError("match-not-found"));

        var match = await _matches.GetByIdAsync(matchId, ct);
        if (match == null)
            return Result.Fail<MatchReplayDto>(new MatchError("match-not-found"));

        var isParty = callerSub == match.Creator.Sub || callerSub == match.Opponent?.Sub;
        if (!isParty)
            return Result.Fail<MatchReplayDto>(new MatchError("forbidden"));

        var dto = _replayBuffer.GetReplay(matchId);
        if (dto == null)
            return Result.Fail<MatchReplayDto>(new MatchError("match-not-found"));

        return Result.Ok(dto);
    }

    // -----------------------------------------------------------------------
    // ToDto — live-balance helper
    // -----------------------------------------------------------------------

    /// <summary>
    /// Converts a <see cref="Match"/> to its wire DTO, computing live clock
    /// balances: the priority holder's remaining time is decremented by the
    /// elapsed wall time since <see cref="Match.PriorityStartedAt"/> (clamped
    /// to zero). The non-holder's balance is returned as-is.
    ///
    /// <paramref name="viewerSub"/> scopes the player <c>DeckSnapshot</c>
    /// field: only the deck's owner sees the full card list. Everyone else
    /// (opponent in-match, lobby browsers, future spectators) gets an
    /// empty list. Pass null for "no viewer" (lobby listings) — both
    /// snapshots are stripped.
    /// </summary>
    public MatchDto ToDto(Match m, string? viewerSub)
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

        // CR 706-adjacent: a player's decklist is private to that player.
        // Lobby browsers and the opposing seat should never see the full
        // mainboard — only the deck's owner gets DeckSnapshot.
        IReadOnlyList<string> creatorSnapshot = (viewerSub != null && viewerSub == m.Creator.Sub)
            ? m.Creator.DeckSnapshot
            : Array.Empty<string>();
        IReadOnlyList<string> opponentSnapshot = (m.Opponent != null && viewerSub != null && viewerSub == m.Opponent.Sub)
            ? m.Opponent.DeckSnapshot
            : Array.Empty<string>();

        return new MatchDto(
            Id: m.Id,
            State: m.State.ToString(),
            Visibility: m.Visibility.ToString(),
            Format: m.Format,
            ClockMinutes: m.ClockMinutes,
            Creator: new MatchPlayerDto(m.Creator.Sub, m.Creator.Handle, m.Creator.DeckId, creatorSnapshot),
            Opponent: m.Opponent is null
                ? null
                : new MatchPlayerDto(m.Opponent.Sub, m.Opponent.Handle, m.Opponent.DeckId, opponentSnapshot),
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
