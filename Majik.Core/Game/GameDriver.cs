using Majik.Core.Abilities;
using Majik.Core.Combat;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Game;

/// <summary>
/// Top-level game loop. Alternates active player each turn, calls
/// <see cref="TurnDriver"/>, and stops as soon as a player has lost OR
/// the turn cap is reached.
///
/// Phase 25 wiring:
///   1. Shuffle each player's library via <see cref="Majik.Core.Random.GameRandom"/>
///   2. Choose starting player — caller-supplied (CR 103.2/103.4/103.7
///      decision made upstream) or, when unspecified, a random coin flip
///   3. Each player runs the <see cref="MulliganController"/> (London)
///   4. Normal turn loop, first player skips draw step (already handled by TurnDriver)
/// </summary>
public sealed class GameDriver
{
    private readonly IReadOnlyList<Player> _players;
    private readonly IReadOnlyDictionary<Player, IPlayerAgent> _agents;
    private readonly TurnDriver _turnDriver;
    private readonly StateBasedActions _sba;
    private readonly Majik.Core.Random.GameRandom _rng;
    private readonly Majik.Core.Events.IEventBus? _eventBus;
    private readonly ZoneService _zoneService;
    private readonly TriggerManager _triggerManager;
    private readonly ExtraTurnQueue _extraTurns = new();

    // CR 720 — per-game registry of "control another player" grants. Effects
    // (Mindslaver, Emrakul) call ControlPlayer.Grant against this; the
    // TurnDriver consumes a pending grant at the controlled player's
    // turn-start and the ControlAware agent map reroutes that player's
    // decisions to the controller for the turn.
    private readonly Majik.Core.Players.ControlPlayerRegistry _controlPlayers = new();

    /// <summary>Effects that grant an extra turn enqueue here; the loop
    /// consults this before round-robin advancement.</summary>
    public ExtraTurnQueue ExtraTurns => _extraTurns;

    /// <summary>CR 720 — registry of active "control another player" grants.
    /// Effects (Mindslaver's activated ability, Emrakul's cast trigger) call
    /// <see cref="Majik.Core.Players.ControlPlayerRegistry.GrantControl"/> on
    /// this to take control of an opponent's next turn.</summary>
    public Majik.Core.Players.ControlPlayerRegistry ControlPlayers => _controlPlayers;

    public sealed record GameResult(int TurnsPlayed, Player? Winner, Player? StartingPlayer);

    public GameDriver(
        IReadOnlyList<Player> players,
        IReadOnlyDictionary<Player, IPlayerAgent> agents,
        Majik.Core.Stack.Stack stack,
        ZoneService zoneService,
        TriggerManager triggerManager,
        StackResolver stackResolver,
        StateBasedActions stateBasedActions,
        PriorityManager priorityManager,
        CombatFlow combatFlow,
        Majik.Core.Random.GameRandom? rng = null,
        Majik.Core.Events.IEventBus? eventBus = null,
        Func<Majik.Core.Cards.ICard, Player, Majik.Core.Stack.Stack?, SpellDefinition?>? spellDefinitionResolver = null,
        Majik.Core.Effects.ContinuousEffectsService? continuousEffects = null,
        LandDropTracker? landDropTracker = null,
        Func<Player, IAutoPassPrefsView?>? autoPassPrefsProvider = null,
        Func<GameContext, bool>? isPassOnlyDeadWindow = null,
        Func<DateTime>? clock = null)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        // CR 720 — route every agent lookup through the control registry so a
        // controlled player's decisions go to the controller's agent during
        // the controlled turn. Transparent when no control is active (the
        // wrapper returns the queried player's own agent). Every downstream
        // consumer (TurnDriver, PriorityLoop, CombatFlow, mulligan / opening-
        // hand subscribers) reads from this single wrapped map.
        _agents = new Majik.Core.Players.Agents.ControlAwareAgentMap(
            agents ?? throw new ArgumentNullException(nameof(agents)),
            _controlPlayers);
        _sba = stateBasedActions ?? throw new ArgumentNullException(nameof(stateBasedActions));
        _rng = rng ?? new Majik.Core.Random.GameRandom();
        _eventBus = eventBus;
        _zoneService = zoneService ?? throw new ArgumentNullException(nameof(zoneService));
        _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));

        // CR 701.20 — register the game RNG + bus so factory closures
        // (e.g. SearchSpellFactory's tutor effects) can resolve the
        // active randomness source and emit LibraryShuffledEvent without
        // a parameter-threading rewrite. AgentRegistry uses the same
        // pattern for per-player agent lookup.
        foreach (var p in _players)
        {
            Majik.Core.Random.GameRandomRegistry.Set(p, _rng);
            if (_eventBus is not null)
            {
                Majik.Core.Events.EventBusRegistry.Set(p, _eventBus);
            }
            // CR 603.6a / CR 614 — register the live ZoneService so tutor
            // / fetch effect closures (PrimevalTitan, Scapeshift,
            // fetchlands, SearchForTomorrow, GreenSunsZenith,
            // ChordOfCalling, EldritchEvolution, …) can route their
            // library → battlefield moves through ZoneService.MoveCard
            // instead of raw zone mutation, so ETB triggers fire and
            // enters-tapped replacements run on tutored permanents.
            Majik.Core.Services.ZoneServiceRegistry.Set(p, _zoneService);
        }
        Majik.Core.Random.GameRandomRegistry.SetDefault(_rng);
        if (_eventBus is not null)
        {
            Majik.Core.Events.EventBusRegistry.SetDefault(_eventBus);
        }
        // NB: deliberately no ZoneServiceRegistry.SetDefault() — per-player
        // registration above is sufficient for tutor closures (they look up
        // by the resolving player's id), and skipping the default keeps
        // unrelated tests (which build a fresh Player and don't seed the
        // registry) from inadvertently inheriting a stale ZoneService from
        // a concurrently-running test collection.

        // CR 305.2 — a full game ALWAYS needs a LandDropTracker so the
        // per-turn one-land cap is enforced in PriorityLoop. Default-
        // construct one when callers don't supply one (older call sites
        // pre-date the param). Without this, bots — whose PlayLand
        // proposals are validated only inside PriorityLoop against this
        // tracker — could play unbounded lands in a single main phase.
        var tracker = landDropTracker ?? new LandDropTracker();

        _turnDriver = new TurnDriver(
            // CR 720 — pass the control-aware wrapped map (not the raw
            // agents) so the driver's per-player agent lookups reroute to
            // the controller during a controlled turn, plus the registry so
            // the driver can consume/clear control at turn boundaries.
            players, _agents, stack, zoneService, triggerManager,
            stackResolver, stateBasedActions, priorityManager, combatFlow,
            eventBus: eventBus,
            spellDefinitionResolver: spellDefinitionResolver,
            continuousEffects: continuousEffects,
            landDropTracker: tracker,
            // Slice 5a — forward server-side auto-pass plumbing down to
            // each per-round PriorityLoop. Null = pre-Slice-5a behaviour.
            autoPassPrefsProvider: autoPassPrefsProvider,
            isPassOnlyDeadWindow: isPassOnlyDeadWindow,
            clock: clock,
            controlRegistry: _controlPlayers);
    }

    public async Task<GameResult> RunGameAsync(
        int maxTurns = 30,
        int? startingPlayerIndex = null,
        CancellationToken ct = default)
    {
        // CR 103.1 — shuffle libraries.
        foreach (var p in _players)
        {
            ShuffleLibrary(p);
        }

        var startingIndex = ResolveStartingIndex(startingPlayerIndex);
        var startingPlayer = _players[startingIndex];

        await RunMulligansAsync(startingPlayer, ct);

        // CR 103.5 — opening-hand check (Leyline alt-cost + reveal-from-hand).
        if (_eventBus is not null)
        {
            await RunOpeningHandCheckAsync(startingIndex);
        }

        return await RunTurnLoopAsync(startingIndex, startingPlayer, maxTurns, ct);
    }

    /// <summary>CR 103.2 / 103.4 / 103.7 — resolve the starting seat. In a
    /// real match the pre-game die roll + play/draw decision is settled
    /// upstream and encoded in <paramref name="startingPlayerIndex"/>. When
    /// no index is supplied (bot-vs-bot sims, unit tests that want CR 103.2
    /// randomness) fall back to a random pick (coin flip for 2 players,
    /// generalised to N).</summary>
    private int ResolveStartingIndex(int? startingPlayerIndex)
    {
        if (startingPlayerIndex is not { } idx) return _rng.Next(_players.Count);
        if (idx < 0 || idx >= _players.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startingPlayerIndex),
                $"startingPlayerIndex {idx} is outside [0,{_players.Count}).");
        }
        return idx;
    }

    /// <summary>CR 103.4 — per-player London mulligan loop.</summary>
    private async Task RunMulligansAsync(Player startingPlayer, CancellationToken ct)
    {
        var mulligan = new MulliganController();
        foreach (var p in _players)
        {
            var ctx = new GameContext(
                p, _players, startingPlayer, 0, PhaseStateType.Untap,
                new Majik.Core.Stack.Stack());
            await mulligan.RunAsync(p, _agents[p], ctx, ct: ct);
        }
    }

    /// <summary>CR 103.5 — attach the opening-hand subscribers (Leyline
    /// alt-cost CR 702.95, reveal-from-opening-hand CR 603.7 follow-ups),
    /// then publish OpeningHandCheckEvent in turn order ("starting with the
    /// starting player").</summary>
    private async Task RunOpeningHandCheckAsync(int startingIndex)
    {
        var leylineAltCost = new OpeningHandLeylineAlternativeCost(_zoneService, _agents);
        leylineAltCost.Attach(_eventBus!);

        // Leyline subscribes first → reveals run after Leylines settle.
        var revealLook4 = new OpeningHandRevealLook4Trigger(_agents, _triggerManager);
        revealLook4.Attach(_eventBus!);

        // Chancellor-style "reveal → add mana at first main phase" riders.
        var revealAddMana = new OpeningHandRevealAddManaTrigger(_agents, _triggerManager);
        revealAddMana.Attach(_eventBus!);

        for (var i = 0; i < _players.Count; i++)
        {
            var seat = _players[(startingIndex + i) % _players.Count];
            var snapshot = seat.Zones.Hand.GetCards().ToList();
            await _eventBus!.PublishAsync(
                new Majik.Core.Events.OpeningHandCheckEvent(seat, snapshot));
        }
    }

    /// <summary>Main turn loop: SBA → loss check → pick next active seat
    /// (CR 500.7 extra-turn queue takes precedence over round-robin) →
    /// RunTurnAsync. Stops when only one (or zero) players remain alive or
    /// the turn cap is hit.</summary>
    private async Task<GameResult> RunTurnLoopAsync(
        int startingIndex, Player startingPlayer, int maxTurns, CancellationToken ct)
    {
        var turnNumber = 0;
        var activeIndex = startingIndex;
        while (turnNumber < maxTurns)
        {
            _sba.CheckStateBasedActions(
                _players,
                _players.SelectMany(p => p.Zones.Battlefield.GetCards()).ToList());

            if (TryFinalizeOnSurvivorCount(turnNumber, startingPlayer) is { } early)
                return early;

            turnNumber++;

            // CR 500.7 — extra-turn queue takes precedence over round-robin.
            Player active;
            if (_extraTurns.TryDequeueNext(out var extra))
            {
                active = extra!;
            }
            else
            {
                active = _players[activeIndex];
                activeIndex = (activeIndex + 1) % _players.Count;
            }
            await _turnDriver.RunTurnAsync(active, turnNumber, ct);
        }

        _sba.CheckStateBasedActions(
            _players,
            _players.SelectMany(p => p.Zones.Battlefield.GetCards()).ToList());
        var stillAlive = _players.Where(p => !p.HasLost).ToList();
        return new GameResult(
            turnNumber, stillAlive.Count == 1 ? stillAlive[0] : null, startingPlayer);
    }

    /// <summary>If exactly one (or zero) players remain alive, build the
    /// terminating <see cref="GameResult"/>. Returns null when the game
    /// should continue.</summary>
    private GameResult? TryFinalizeOnSurvivorCount(int turnNumber, Player startingPlayer)
    {
        var alive = _players.Where(p => !p.HasLost).ToList();
        if (alive.Count == 1) return new GameResult(turnNumber, alive[0], startingPlayer);
        if (alive.Count == 0) return new GameResult(turnNumber, null, startingPlayer);
        return null;
    }

    private void ShuffleLibrary(Player p)
    {
        // CR 103.1 — pre-game shuffle. Use the new IZone.Shuffle primitive
        // so the shared LibraryShuffle helper publishes a single
        // LibraryShuffledEvent ("game-start" reason) on the event bus.
        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(p, "game-start");
    }
}
