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
///   2. Choose starting player (coin flip)
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
    private readonly ExtraTurnQueue _extraTurns = new();

    /// <summary>Effects that grant an extra turn enqueue here; the loop
    /// consults this before round-robin advancement.</summary>
    public ExtraTurnQueue ExtraTurns => _extraTurns;

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
        LandDropTracker? landDropTracker = null)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _sba = stateBasedActions ?? throw new ArgumentNullException(nameof(stateBasedActions));
        _rng = rng ?? new Majik.Core.Random.GameRandom();
        _eventBus = eventBus;
        _zoneService = zoneService ?? throw new ArgumentNullException(nameof(zoneService));

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
            players, agents, stack, zoneService, triggerManager,
            stackResolver, stateBasedActions, priorityManager, combatFlow,
            eventBus: eventBus,
            spellDefinitionResolver: spellDefinitionResolver,
            continuousEffects: continuousEffects,
            landDropTracker: tracker);
    }

    public async Task<GameResult> RunGameAsync(int maxTurns = 30, CancellationToken ct = default)
    {
        // CR 103.1 — shuffle libraries.
        foreach (var p in _players)
        {
            ShuffleLibrary(p);
        }

        // CR 103.2 — determine starting player. Coin flip works for 2,
        // generalised to N-player by random pick.
        var startingIndex = _rng.Next(_players.Count);
        var startingPlayer = _players[startingIndex];

        // CR 103.4 — mulligan loop per player.
        var mulligan = new MulliganController();
        foreach (var p in _players)
        {
            var ctx = new GameContext(
                p, _players, startingPlayer, 0, PhaseStateType.Untap,
                new Majik.Core.Stack.Stack());
            await mulligan.RunAsync(p, _agents[p], ctx, ct: ct);
        }

        // CR 103.5 — opening-hand check. Fired AFTER all mulligans
        // resolve, in starting-player-first turn order. The Leyline
        // cycle's opening-hand alt-cost (CR 702.95) subscribes to this
        // event via OpeningHandLeylineAlternativeCost; the subscriber
        // walks each player's hand for KeywordAbility("OpeningHandLeyline")
        // markers and prompts via IPlayerAgent.ChooseYesNoAsync.
        if (_eventBus is not null)
        {
            var leylineAltCost = new OpeningHandLeylineAlternativeCost(
                _zoneService, _agents);
            leylineAltCost.Attach(_eventBus);

            // Publish in turn order: starting player first, then the
            // remaining seats cycling forward. Matches CR 103.5
            // ("starting with the starting player").
            for (var i = 0; i < _players.Count; i++)
            {
                var seat = _players[(startingIndex + i) % _players.Count];
                var snapshot = seat.Zones.Hand.GetCards().ToList();
                await _eventBus.PublishAsync(
                    new Majik.Core.Events.OpeningHandCheckEvent(seat, snapshot));
            }
        }

        var turnNumber = 0;
        var activeIndex = startingIndex;
        while (turnNumber < maxTurns)
        {
            _sba.CheckStateBasedActions(
                _players,
                _players.SelectMany(p => p.Zones.Battlefield.GetCards()).ToList());

            var alive = _players.Where(p => !p.HasLost).ToList();
            if (alive.Count == 1) return new GameResult(turnNumber, alive[0], startingPlayer);
            if (alive.Count == 0) return new GameResult(turnNumber, null, startingPlayer);

            turnNumber++;

            // CR 500.7 — check extra-turn queue before round-robin pickup.
            Player active;
            if (_extraTurns.TryDequeueNext(out var extra))
            {
                active = extra!;
                // Active player index doesn't advance for an extra turn;
                // the queue may interrupt the normal cadence multiple
                // times in a row.
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

    private void ShuffleLibrary(Player p)
    {
        // CR 103.1 — pre-game shuffle. Use the new IZone.Shuffle primitive
        // so the shared LibraryShuffle helper publishes a single
        // LibraryShuffledEvent ("game-start" reason) on the event bus.
        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(p, "game-start");
    }
}
