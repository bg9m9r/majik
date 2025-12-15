using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Game;

/// <summary>
/// Manages turn order, turn transitions, and extra turns.
/// </summary>
public class TurnManager
{
    private readonly List<Player> _players;
    private readonly Queue<Player> _extraTurns = new();
    private readonly IEventBus? _eventBus;
    
    private int _currentPlayerIndex;
    private int _turnNumber;
    private Player? _activePlayer;
    private bool _isFirstTurn;

    /// <summary>
    /// The active player (whose turn it is).
    /// </summary>
    public Player? ActivePlayer => _activePlayer;

    /// <summary>
    /// The current turn number.
    /// </summary>
    public int TurnNumber => _turnNumber;

    /// <summary>
    /// Whether this is the first turn of the game.
    /// </summary>
    public bool IsFirstTurn => _isFirstTurn;

    /// <summary>
    /// Whether there are extra turns in the queue.
    /// </summary>
    public bool HasExtraTurns => _extraTurns.Count > 0;

    public TurnManager(List<Player> players, IEventBus? eventBus = null)
    {
        if (players == null || players.Count < 2)
        {
            throw new ArgumentException("TurnManager requires at least 2 players", nameof(players));
        }

        _players = players;
        _eventBus = eventBus;
        _currentPlayerIndex = 0;
        _turnNumber = 0;
        _isFirstTurn = true;
    }

    /// <summary>
    /// Start a turn for the specified player.
    /// </summary>
    public void StartTurn(Player player)
    {
        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (!_players.Contains(player))
        {
            throw new InvalidGameStateException($"Player {player.Name} is not in the game");
        }

        _activePlayer = player;
        _turnNumber++;
        
        _eventBus?.Publish(new TurnStartedEvent(player, _turnNumber));
    }

    /// <summary>
    /// End the current turn.
    /// </summary>
    public void EndTurn()
    {
        if (_activePlayer == null)
        {
            throw new InvalidGameStateException("Cannot end turn: no active player");
        }

        var endingPlayer = _activePlayer;
        var endingTurnNumber = _turnNumber;

        _eventBus?.Publish(new TurnEndedEvent(endingPlayer, endingTurnNumber));

        _activePlayer = null;
        _isFirstTurn = false;
    }

    /// <summary>
    /// Start the next turn.
    /// </summary>
    public void StartNextTurn()
    {
        // Check for extra turns first
        if (_extraTurns.Count > 0)
        {
            var extraTurnPlayer = _extraTurns.Dequeue();
            StartTurn(extraTurnPlayer);
            return;
        }

        // Move to next player in rotation
        _currentPlayerIndex = (_currentPlayerIndex + 1) % _players.Count;
        var nextPlayer = _players[_currentPlayerIndex];
        StartTurn(nextPlayer);
    }

    /// <summary>
    /// Add an extra turn for the specified player.
    /// </summary>
    public void AddExtraTurn(Player player)
    {
        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (!_players.Contains(player))
        {
            throw new InvalidGameStateException($"Player {player.Name} is not in the game");
        }

        _extraTurns.Enqueue(player);
        _eventBus?.Publish(new ExtraTurnAddedEvent(player));
    }

    /// <summary>
    /// Get the next player in turn order.
    /// </summary>
    public Player GetNextPlayer()
    {
        if (_extraTurns.Count > 0)
        {
            return _extraTurns.Peek();
        }

        var nextIndex = (_currentPlayerIndex + 1) % _players.Count;
        return _players[nextIndex];
    }

    /// <summary>
    /// Initialize the first turn.
    /// </summary>
    public void InitializeFirstTurn()
    {
        if (_turnNumber > 0)
        {
            throw new InvalidGameStateException("Cannot initialize first turn: game has already started");
        }

        _currentPlayerIndex = 0;
        StartTurn(_players[0]);
    }
}
