using Majik.Core.Domain.DomainEvents;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Domain.ValueObjects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Game;

/// <summary>
/// Manages priority passing between players.
/// Implements Magic: The Gathering priority rules (Rule 117).
/// </summary>
public class PriorityManager
{
    private readonly List<Player> _players;
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly IEventBus? _eventBus;
    
    private PriorityState _state;

    /// <summary>
    /// The player who currently has priority.
    /// </summary>
    public Player? CurrentPlayer => _state.CurrentPlayer;

    /// <summary>
    /// Whether all players have passed in succession.
    /// </summary>
    public bool AllPlayersPassed => _state.AllPlayersPassed;

    public PriorityManager(List<Player> players, Majik.Core.Stack.Stack stack, IEventBus? eventBus = null)
    {
        if (players == null || players.Count < 2)
        {
            throw new ArgumentException("PriorityManager requires at least 2 players", nameof(players));
        }

        _players = players;
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _eventBus = eventBus;
        _state = PriorityState.Reset(players.Count);
    }

    /// <summary>
    /// Initialize priority for a new phase or step.
    /// Active player receives priority first (Rule 117.3a).
    /// </summary>
    public void InitializeForPhase(Player activePlayer)
    {
        if (activePlayer == null)
        {
            throw new ArgumentNullException(nameof(activePlayer));
        }

        if (!_players.Contains(activePlayer))
        {
            throw new InvalidGameStateException($"Player {activePlayer.Name} is not in the game");
        }

        _state = PriorityState.Initial(activePlayer, _players.Count);

        _eventBus?.Publish(new PriorityReceivedEvent(activePlayer));
    }

    /// <summary>
    /// Give priority to a specific player.
    /// </summary>
    public void GivePriority(Player player)
    {
        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (!_players.Contains(player))
        {
            throw new InvalidGameStateException($"Player {player.Name} is not in the game");
        }

        _state = _state.WithCurrentPlayer(player);

        _eventBus?.Publish(new PriorityReceivedEvent(player));
    }

    /// <summary>
    /// Pass priority to the next player.
    /// </summary>
    public void PassPriority()
    {
        if (_state.CurrentPlayer == null)
        {
            throw new InvalidGameStateException("Cannot pass priority: no current player");
        }

        var passingPlayer = _state.CurrentPlayer;
        _state = _state.WithPassIncremented();

        _eventBus?.Publish(new PriorityPassedEvent(passingPlayer));

        // If all players have passed, check if we can resolve stack or end phase
        if (_state.AllPlayersPassed)
        {
            _eventBus?.Publish(new AllPlayersPassedEvent(_stack.IsEmpty));
            return;
        }

        // Move to next player in turn order (APNAP)
        var currentIndex = _players.IndexOf(_state.CurrentPlayer!);
        var nextIndex = (currentIndex + 1) % _players.Count;
        var nextPlayer = _players[nextIndex];

        _state = _state.WithCurrentPlayerKeepPassCount(nextPlayer);
        _eventBus?.Publish(new PriorityReceivedEvent(nextPlayer));
    }

    /// <summary>
    /// Hold priority (player keeps priority after casting/activating).
    /// Rule 117.3c: Player receives priority after casting/activating.
    /// </summary>
    public void HoldPriority()
    {
        if (_state.CurrentPlayer == null)
        {
            throw new InvalidGameStateException("Cannot hold priority: no current player");
        }

        _state = _state.WithPassReset();
        // Player keeps priority (no change to CurrentPlayer)
    }

    /// <summary>
    /// Check if the current phase can end.
    /// Phase can end if stack is empty and all players have passed (Rule 117.4).
    /// </summary>
    public bool CanEndPhase()
    {
        return _stack.IsEmpty && _state.AllPlayersPassed;
    }

    /// <summary>
    /// Get the next player in priority order (APNAP).
    /// </summary>
    public Player? GetNextPlayer()
    {
        if (_state.CurrentPlayer == null)
        {
            return _state.ActivePlayer;
        }

        var currentIndex = _players.IndexOf(_state.CurrentPlayer);
        var nextIndex = (currentIndex + 1) % _players.Count;
        return _players[nextIndex];
    }

    /// <summary>
    /// Reset priority state (for new phase/step).
    /// </summary>
    public void Reset()
    {
        _state = PriorityState.Reset(_players.Count);
    }
}
