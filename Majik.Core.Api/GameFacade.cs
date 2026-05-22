using System.Text.Json;
using Majik.Core.Abilities;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Api;

/// <summary>
/// Per-game aggregate exposed to the outside world. Wraps a live engine
/// (event bus, stack, players, trigger manager, priority loop) with a
/// command/state/events interface that's safe to ship over HTTP/JSON.
///
/// Two run modes:
/// - <see cref="StartAsync"/> runs a single priority round on Alice
///   (legacy Phase-9 behavior used by most existing tests).
/// - <see cref="StartFullGameAsync"/> runs the full
///   <see cref="GameDriver"/> loop: shuffle, mulligan, multi-turn,
///   state-based actions, combat, win-condition checks (Phase 10).
/// </summary>
public sealed class GameFacade
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly PriorityManager _priority;
    private readonly StateBasedActions _sba;
    private readonly CombatFlow _combatFlow;
    private readonly Player _alice;
    private readonly Player _bob;
    private readonly RemoteAgent _aliceAgent;
    private readonly RemoteAgent _bobAgent;
    private IPlayerAgent _aliceAgentEffective;
    private IPlayerAgent _bobAgentEffective;
    private PriorityLoop _loop;
    private Task? _loopTask;
    private Task<GameDriver.GameResult>? _fullGameTask;
    private readonly List<Action<EventDto>> _subscribers = new();
    private readonly ActionLog _log = new();

    // Synchronization signal that pulses every time an agent registers a
    // new prompt. StartAsync / SubmitAsync capture this BEFORE poking the
    // engine, then await it — so they return only once the engine has
    // settled at its next prompt (or the round/game has finished). This
    // replaces the previous `Task.Delay(1)` magic sleep, which was racy
    // on slow CI runners and produced flaky "Agent has no pending prompt"
    // failures on subsequent submits.
    private TaskCompletionSource<bool> _nextPromptSignal = NewSignal();

    private static TaskCompletionSource<bool> NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void PulsePromptSignal()
    {
        var prev = Interlocked.Exchange(ref _nextPromptSignal, NewSignal());
        prev.TrySetResult(true);
    }
    private int _currentTurn = 1;
    private PhaseStateType _currentPhase = PhaseStateType.Main;
    private Player? _currentActivePlayer;

    public Guid GameId { get; } = Guid.NewGuid();

    public bool IsRoundComplete => _loopTask?.IsCompleted ?? false;

    /// <summary>The Alice/creator-slot player. Exposed so tooling can wire
    /// a non-RemoteAgent (e.g. <see cref="Majik.Bot.BotPlayerAgent"/>) before
    /// the game starts.</summary>
    public Player Alice => _alice;

    /// <summary>The Bob/opponent-slot player. See <see cref="Alice"/>.</summary>
    public Player Bob => _bob;

    /// <summary>
    /// The game's replacement-effect bus. Binders (e.g. ShockLandBinder)
    /// register handlers here during deck load; ZoneService reads from it on
    /// every card move so replacements fire correctly at the table.
    /// </summary>
    public ReplacementBus Replacements { get; } = new();

    /// <summary>
    /// CR 613 layer-system executor for this game. Binders that produce
    /// keyword-style continuous effects (Prowess pump, until-end-of-turn
    /// buffs/debuffs, combat restrictions) register onto this instance;
    /// creatures consult it via <see cref="Majik.Core.Cards.Creature.ActiveEffects"/>
    /// to compute current characteristics. TurnDriver expires EOT effects
    /// during the cleanup step.
    /// </summary>
    public ContinuousEffectsService ContinuousEffects { get; } = new();

    private GameFacade(Player alice, Player bob)
    {
        _alice = alice;
        _bob = bob;

        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _zones = new ZoneService(_bus, Replacements);
        _sba = new StateBasedActions(_bus, _zones, _triggers);
        _resolver = new StackResolver(_bus, _zones, _sba);
        _priority = new PriorityManager(new List<Player> { alice, bob }, _stack, _bus, _triggers);
        _combatFlow = new CombatFlow(_bus, _sba, Replacements);

        _aliceAgent = new RemoteAgent(alice, LookupCard);
        _bobAgent = new RemoteAgent(bob, LookupCard);
        _aliceAgent.PromptRequested += _ => PulsePromptSignal();
        _bobAgent.PromptRequested += _ => PulsePromptSignal();

        _aliceAgentEffective = _aliceAgent;
        _bobAgentEffective = _bobAgent;

        _loop = BuildPriorityLoop();

        _bus.SubscribeAll(BridgeEvent);
        _bus.Subscribe<TurnStartedEvent>(e => { _currentTurn = e.TurnNumber; _currentActivePlayer = e.Player; });
        _bus.Subscribe<PhaseStartedEvent>(e => { _currentPhase = e.PhaseType; });
        _bus.Subscribe<StepStartedEvent>(e => { _currentPhase = e.StepType; });
    }

    private PriorityLoop BuildPriorityLoop() => new PriorityLoop(
        players: new[] { _alice, _bob },
        priority: _priority,
        stack: _stack,
        stackResolver: _resolver,
        zoneService: _zones,
        agents: new Dictionary<Player, IPlayerAgent>
        {
            [_alice] = _aliceAgentEffective,
            [_bob] = _bobAgentEffective,
        },
        turnNumberAccessor: () => 1,
        phaseAccessor: () => PhaseStateType.Main);

    /// <summary>Replaces the Alice-seat agent (typically with a
    /// <see cref="Majik.Bot.BotPlayerAgent"/>). Must be called before
    /// <see cref="StartAsync"/> / <see cref="StartFullGameAsync"/>; throws
    /// once the game is running. After swap, <see cref="SubmitAsync"/> for
    /// that seat will throw — the bot drives itself.</summary>
    public void ReplaceAliceAgent(IPlayerAgent agent) => SwapAgent(_alice, agent);

    /// <summary>Replaces the Bob-seat agent. See <see cref="ReplaceAliceAgent"/>.</summary>
    public void ReplaceBobAgent(IPlayerAgent agent) => SwapAgent(_bob, agent);

    private void SwapAgent(Player seat, IPlayerAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (_loopTask != null || _fullGameTask != null)
            throw new InvalidOperationException("Cannot replace agent after the game has started.");

        if (ReferenceEquals(seat, _alice)) _aliceAgentEffective = agent;
        else _bobAgentEffective = agent;

        _loop = BuildPriorityLoop();
    }

    public static GameFacade Create(
        string aliceName,
        string bobName,
        IReadOnlyList<ICard> aliceDeck,
        IReadOnlyList<ICard> bobDeck,
        ICardRepository? cardRepo = null,
        ReplacementBus? replacements = null)
    {
        var alice = new Player(aliceName, 20);
        var bob = new Player(bobName, 20);

        // Construct the facade first so its internal ReplacementBus is available.
        // Any externally-supplied bus is ignored — binders register onto the
        // facade's own bus, which ZoneService already holds a reference to.
        var facade = new GameFacade(alice, bob);
        var bus = facade.Replacements;

        foreach (var card in aliceDeck)
        {
            card.SetOwner(alice);
            BindCardAbilities(card, alice, cardRepo, bus, facade.ContinuousEffects);
            alice.Zones.GetZone(ZoneType.Library).AddCard(card);
        }
        foreach (var card in bobDeck)
        {
            card.SetOwner(bob);
            BindCardAbilities(card, bob, cardRepo, bus, facade.ContinuousEffects);
            bob.Zones.GetZone(ZoneType.Library).AddCard(card);
        }

        return facade;
    }

    /// <summary>
    /// Attaches abilities to a card after its owner is set. When
    /// <paramref name="cardRepo"/> is provided the full binder pipeline
    /// runs (KeywordBinder, OracleManaBinder, AffinityBinder, SagaBinder,
    /// OracleTriggeredAbilityBinder, ShockLandBinder). Without a repo only
    /// the basic-land mana path fires — preserving pre-existing behaviour.
    ///
    /// <paramref name="replacements"/> is the game's own ReplacementBus and
    /// is always non-null when called from <see cref="Create"/>; ShockLandBinder
    /// registers onto it unconditionally whenever the repo returns a matching
    /// shock-land entity (CR 614).
    /// </summary>
    private static void BindCardAbilities(
        ICard card,
        Player controller,
        ICardRepository? cardRepo,
        ReplacementBus replacements,
        ContinuousEffectsService effects)
    {
        // Every creature consults the game's CES for current P/T and keywords
        // (CR 613). Hook it up regardless of repo presence so vanilla creatures
        // still get layer-system computation when other effects target them.
        if (card is Majik.Core.Cards.Creature creature)
        {
            creature.ActiveEffects = effects;
        }

        if (cardRepo != null)
        {
            var entity = cardRepo.GetByName(card.Name);
            if (entity != null)
            {
                KeywordBinder.Bind(card, entity, controller, effects);
                OracleManaBinder.Bind(card, entity, controller);
                AffinityBinder.Bind(card, entity);
                SagaBinder.Bind(card, entity);
                foreach (var trig in OracleTriggeredAbilityBinder.Bind(card, entity, controller))
                {
                    card.AddAbility(trig);
                }
                ShockLandBinder.Bind(card, entity, replacements);
                OracleLandActivatedAbilityBinder.Bind(card, entity, controller);
                OracleLoyaltyAbilityBinder.Bind(card, entity, controller);
                return;
            }
        }

        // Fallback: no repo or unknown card — attach basic-land mana only.
        OracleManaBinder.BindBasicLandMana(card, controller);
    }

    /// <summary>
    /// Kicks off the priority round. Returns immediately; the loop awaits on
    /// the first player's agent. Submit commands via <see cref="SubmitAsync"/>
    /// to advance.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_loopTask != null)
        {
            throw new InvalidOperationException("Game already started.");
        }

        // Capture the signal BEFORE kicking the loop. The loop runs
        // synchronously up to its first agent.Prompt<T> call, which fires
        // PromptRequested → PulsePromptSignal completes the captured TCS.
        var settled = _nextPromptSignal.Task;
        _loopTask = _loop.RunUntilRoundEndsAsync(_alice, ct);
        await WaitForPromptOrLoopAsync(settled, _loopTask, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Run the full <see cref="GameDriver"/> loop. Returns immediately;
    /// the driver runs in the background and reads its decisions from
    /// the two <see cref="RemoteAgent"/>s (so callers drive it via
    /// <see cref="SubmitAsync"/>). Use <see cref="FullGameTask"/> to
    /// await completion.
    ///
    /// Phase 10 entry point — shuffle, mulligan, multi-turn, SBAs,
    /// combat, win conditions all handled by the driver.
    /// </summary>
    public Task StartFullGameAsync(
        int firstPlayerSlot = 0,
        int maxTurns = 30,
        CancellationToken ct = default)
    {
        if (_loopTask != null || _fullGameTask != null)
        {
            throw new InvalidOperationException("Game already started.");
        }

        var orderedPlayers = firstPlayerSlot == 1
            ? new[] { _bob, _alice }
            : new[] { _alice, _bob };

        var driver = new GameDriver(
            players: orderedPlayers,
            agents: new Dictionary<Player, IPlayerAgent>
            {
                [_alice] = _aliceAgentEffective,
                [_bob] = _bobAgentEffective,
            },
            stack: _stack,
            zoneService: _zones,
            triggerManager: _triggers,
            stackResolver: _resolver,
            stateBasedActions: _sba,
            priorityManager: _priority,
            combatFlow: _combatFlow,
            eventBus: _bus,
            continuousEffects: ContinuousEffects);

        var settled = _nextPromptSignal.Task;
        _fullGameTask = driver.RunGameAsync(maxTurns, ct);
        return WaitForPromptOrLoopAsync(settled, _fullGameTask, ct);
    }

    /// <summary>Task representing the in-flight full-game run, or null
    /// when only <see cref="StartAsync"/> was called.</summary>
    public Task<GameDriver.GameResult>? FullGameTask => _fullGameTask;

    /// <summary>
    /// Submit a player command. Returns when the engine has processed it
    /// (which may include resolving stack objects and reaching the next
    /// prompt).
    /// </summary>
    public async Task SubmitAsync(GameCommand command, CancellationToken ct = default)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));

        var agent = command.PlayerId == _alice.Id ? _aliceAgent
                  : command.PlayerId == _bob.Id ? _bobAgent
                  : throw new InvalidOperationException($"Unknown player {command.PlayerId}.");

        // After a seat-swap the effective agent for that seat is no longer
        // the RemoteAgent — the bot drives itself, so external Submit calls
        // are nonsensical.
        var effective = command.PlayerId == _alice.Id ? _aliceAgentEffective : _bobAgentEffective;
        if (!ReferenceEquals(effective, agent))
            throw new InvalidOperationException("Cannot submit to a bot seat.");

        // Capture the signal BEFORE submitting. agent.Submit resolves the
        // TCS the engine is awaiting; the engine then resumes on the
        // threadpool and (typically) reaches the next agent prompt, which
        // pulses our signal. Awaiting here guarantees the next SubmitAsync
        // sees a registered prompt instead of "Agent has no pending prompt".
        var settled = _nextPromptSignal.Task;

        agent.Submit(command);
        _log.Append(command);

        var loop = _loopTask ?? _fullGameTask;
        await WaitForPromptOrLoopAsync(settled, loop, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for either the next-prompt signal to fire or the active
    /// engine task to complete (whichever comes first). Re-throws if the
    /// engine task ended in a faulted state so the test/HTTP caller sees
    /// the real exception instead of a silent hang on the next submit.
    /// </summary>
    private static async Task WaitForPromptOrLoopAsync(
        Task settled,
        Task? loop,
        CancellationToken ct)
    {
        if (loop == null)
        {
            await settled.WaitAsync(ct).ConfigureAwait(false);
            return;
        }
        var done = await Task.WhenAny(settled, loop).ConfigureAwait(false);
        if (done == loop && loop.IsFaulted)
        {
            await loop.ConfigureAwait(false); // surface the exception
        }
    }

    /// <summary>Append-only log of every command submitted via SubmitAsync.</summary>
    public ActionLog Log => _log;

    /// <summary>
    /// Serializes the current state to JSON bytes — read-only spectator
    /// snapshot. Pair with <see cref="SpectatorSnapshot.Load"/>.
    /// </summary>
    public byte[] Save() => JsonSerializer.SerializeToUtf8Bytes(GetState());

    /// <summary>
    /// Full snapshot including action log. Pair with
    /// <see cref="GameSnapshot"/> deserialization for replay.
    /// </summary>
    public GameSnapshot SaveSnapshot() => new(
        State: GetState(),
        Log: _log.Actions.Select(a => new LoggedCommand(a.At, a.Command)).ToList());

    public byte[] SaveSnapshotBytes() => JsonSerializer.SerializeToUtf8Bytes(SaveSnapshot());

    /// <summary>Full-information snapshot (spectator view). Use
    /// <see cref="GetStateFor"/> for a per-player view that masks
    /// opponent hidden zones.</summary>
    public GameStateDto GetState() => StateSnapshotter.Snapshot(
        GameId, turnNumber: _currentTurn, phase: _currentPhase,
        activePlayer: _currentActivePlayer ?? _priority.CurrentPlayer ?? _alice,
        players: new[] { _alice, _bob },
        stack: _stack);

    /// <summary>Per-player snapshot. CR 706 hidden information (opponent
    /// hand) is masked. Pass the requesting player's id; returns null
    /// when the id matches no slot in this game.</summary>
    public GameStateDto? GetStateFor(Guid viewerPlayerId)
    {
        var viewer = ResolveSlot(viewerPlayerId);
        if (viewer == null) return null;

        return StateSnapshotter.Snapshot(
            GameId, turnNumber: _currentTurn, phase: _currentPhase,
            activePlayer: _currentActivePlayer ?? _priority.CurrentPlayer ?? _alice,
            players: new[] { _alice, _bob },
            stack: _stack,
            viewer: viewer);
    }

    private Player? ResolveSlot(Guid id)
    {
        if (_alice.Id == id) return _alice;
        if (_bob.Id == id) return _bob;
        return null;
    }

    /// <summary>
    /// Stream events. Returned <see cref="IDisposable"/> unsubscribes.
    /// </summary>
    public IDisposable Subscribe(Action<EventDto> handler)
    {
        _subscribers.Add(handler);
        return new Subscription(() => _subscribers.Remove(handler));
    }

    /// <summary>
    /// Subscribe to prompt envelopes. Fires once each time the engine
    /// transitions to awaiting a command from either player. Returned
    /// <see cref="IDisposable"/> detaches the handler.
    /// </summary>
    public IDisposable SubscribePrompts(Action<PromptDto> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Action<IReadOnlyList<Type>> aliceHandler = kinds => handler(BuildPrompt(_alice, kinds));
        Action<IReadOnlyList<Type>> bobHandler = kinds => handler(BuildPrompt(_bob, kinds));
        _aliceAgent.PromptRequested += aliceHandler;
        _bobAgent.PromptRequested += bobHandler;
        return new Subscription(() =>
        {
            _aliceAgent.PromptRequested -= aliceHandler;
            _bobAgent.PromptRequested -= bobHandler;
        });
    }

    private PromptDto BuildPrompt(Player player, IReadOnlyList<Type> kinds)
        => new(GameId, player.Id, kinds.Select(t => t.Name).ToList());

    private void BridgeEvent(GameEvent e)
    {
        var dto = new EventDto(
            EventId: e.EventId,
            Type: e.GetType().Name,
            At: e.Timestamp,
            Payload: EventPayloadBuilder.Build(e));

        foreach (var sub in _subscribers.ToList())
        {
            sub(dto);
        }
    }

    private ICard? LookupCard(Guid instanceId)
    {
        foreach (var player in new[] { _alice, _bob })
        {
            foreach (var zoneType in new[] { ZoneType.Hand, ZoneType.Battlefield, ZoneType.Graveyard, ZoneType.Library, ZoneType.Exile, ZoneType.Stack })
            {
                foreach (var card in player.Zones.GetZone(zoneType).GetCards())
                {
                    if (card.InstanceId == instanceId)
                    {
                        return card;
                    }
                }
            }
        }

        return null;
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _dispose;
        public Subscription(Action dispose) { _dispose = dispose; }
        public void Dispose() => _dispose();
    }
}
