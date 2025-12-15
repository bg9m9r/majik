using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Stack;
using Majik.Core.StateMachine;

namespace Majik.Core.Domain.Aggregates;

/// <summary>
/// Game aggregate root.
/// Manages overall game state, players, and coordinates state machines.
/// Encapsulates game invariants and business rules.
/// </summary>
public class Game
{
    private readonly List<Player> _players = new();
    private readonly IEventBus _eventBus;
    private readonly GameStateMachine _stateMachine;
    private readonly GameService _gameService;
    private readonly PlayerService _playerService;
    private readonly ZoneService _zoneService;
    private TurnManager? _turnManager;
    private readonly PhaseManager _phaseManager;
    private Majik.Core.Stack.Stack? _stack;
    private PriorityManager? _priorityManager;
    private readonly StackResolver _stackResolver;

    /// <summary>
    /// All players in the game.
    /// </summary>
    public IReadOnlyList<Player> Players => _players.AsReadOnly();

    /// <summary>
    /// The active player (whose turn it is).
    /// </summary>
    public Player? ActivePlayer { get; private set; }

    /// <summary>
    /// The current turn number.
    /// </summary>
    public int TurnNumber { get; private set; }

    /// <summary>
    /// The game state machine.
    /// </summary>
    public GameStateMachine StateMachine => _stateMachine;

    /// <summary>
    /// The event bus for this game.
    /// </summary>
    public IEventBus EventBus => _eventBus;

    /// <summary>
    /// Whether the game has started.
    /// </summary>
    public bool IsStarted { get; private set; }

    /// <summary>
    /// The game service.
    /// </summary>
    public GameService GameService => _gameService;

    /// <summary>
    /// The player service.
    /// </summary>
    public PlayerService PlayerService => _playerService;

    /// <summary>
    /// The zone service.
    /// </summary>
    public ZoneService ZoneService => _zoneService;

    /// <summary>
    /// The turn manager.
    /// </summary>
    public TurnManager TurnManager => _turnManager ?? throw new InvalidGameStateException("TurnManager not initialized. Add players and start game first.");

    /// <summary>
    /// The phase manager.
    /// </summary>
    public PhaseManager PhaseManager => _phaseManager;

    /// <summary>
    /// The stack.
    /// </summary>
    public Majik.Core.Stack.Stack Stack => _stack ?? throw new InvalidGameStateException("Stack not initialized. Start game first.");

    /// <summary>
    /// The priority manager.
    /// </summary>
    public PriorityManager PriorityManager => _priorityManager ?? throw new InvalidGameStateException("PriorityManager not initialized. Start game first.");

    public Game(IEventBus? eventBus = null)
    {
        _eventBus = eventBus ?? new Events.EventBus();
        _stateMachine = new GameStateMachine(_eventBus);
        _gameService = new GameService(_eventBus);
        _playerService = new PlayerService(_eventBus);
        _zoneService = new ZoneService(_eventBus);
        _phaseManager = new PhaseManager(_eventBus);
        _stackResolver = new StackResolver(_eventBus);
        TurnNumber = 0;
    }

    /// <summary>
    /// Add a player to the game.
    /// </summary>
    public void AddPlayer(string name, int startingLife = 20)
    {
        if (IsStarted)
        {
            throw new InvalidGameStateException("Cannot add players after game has started.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Player name cannot be null or empty", nameof(name));
        }

        var player = _playerService.CreatePlayer(name, startingLife);
        _players.Add(player);
    }

    /// <summary>
    /// Start the game.
    /// </summary>
    public void StartGame()
    {
        if (IsStarted)
        {
            throw new InvalidGameStateException("Game has already started.");
        }

        if (_players.Count < 2)
        {
            throw new InvalidGameStateException("Need at least 2 players to start the game.");
        }

        IsStarted = true;
        // Transition through game states
        _stateMachine.TransitionTo(GameStateType.Initializing);
        _stateMachine.TransitionTo(GameStateType.Mulligan);
        
        // For now, automatically transition to Playing
        // In a full implementation, we'd wait for mulligan decisions
        _stateMachine.TransitionTo(GameStateType.Playing);
        
        // Initialize turn manager (now that we have players)
        _turnManager = new TurnManager(_players, _eventBus);
        
        // Initialize stack and priority manager
        _stack = new Majik.Core.Stack.Stack(_eventBus);
        _priorityManager = new PriorityManager(_players, _stack, _eventBus);
        
        // Initialize turn and phase managers
        _turnManager.InitializeFirstTurn();
        _phaseManager.InitializeForTurn(_players[0], true);
        
        // Set first player as active
        ActivePlayer = _players[0];
        TurnNumber = 1;
        
            // Start first phase
            _phaseManager.StartFirstPhase();
            
            // Process first turn phases (with stack and priority)
            _phaseManager.ProcessAllPhases(_stack, _priorityManager);
            
            _eventBus.Publish(new GameStartedEvent());
    }

    /// <summary>
    /// Get a player by name.
    /// </summary>
    public Player? GetPlayer(string name)
    {
        return _players.FirstOrDefault(p => p.Name == name);
    }

    /// <summary>
    /// Update the game state (called each game cycle).
    /// </summary>
    public void Update()
    {
        _stateMachine.Update();
    }

    /// <summary>
    /// Advance to the next phase.
    /// </summary>
    public void AdvancePhase()
    {
        if (!_phaseManager.CanTransition)
        {
            return;
        }

        _phaseManager.TransitionToNextPhase();

        // If turn is complete, advance to next turn
        if (_phaseManager.IsTurnComplete())
        {
            AdvanceTurn();
        }
    }

    /// <summary>
    /// Advance to the next turn and automatically process all phases.
    /// </summary>
    public void AdvanceTurn()
    {
        if (_turnManager == null || _turnManager.ActivePlayer == null)
        {
            return;
        }

        _turnManager.EndTurn();
        _turnManager.StartNextTurn();
        
        // Update active player and turn number
        var nextActivePlayer = _turnManager.ActivePlayer;
        if (nextActivePlayer != null)
        {
            ActivePlayer = nextActivePlayer;
            TurnNumber = _turnManager.TurnNumber;

            // Initialize phase manager for new turn
            _phaseManager.InitializeForTurn(nextActivePlayer, _turnManager.IsFirstTurn);
            _phaseManager.StartFirstPhase();
            
            // Automatically process all phases in the turn (with stack and priority)
            _phaseManager.ProcessAllPhases(_stack, _priorityManager);
        }
    }

    /// <summary>
    /// Process a complete turn cycle (all phases).
    /// </summary>
    public void ProcessTurnCycle()
    {
        while (!_phaseManager.IsTurnComplete())
        {
            AdvancePhase();
        }
    }
}
