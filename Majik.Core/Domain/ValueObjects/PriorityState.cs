using Majik.Core.Players;

namespace Majik.Core.Domain.ValueObjects;

/// <summary>
/// Value object representing the current priority state.
/// Immutable encapsulation of priority information.
/// </summary>
public class PriorityState : IEquatable<PriorityState>
{
    /// <summary>
    /// The player who currently has priority.
    /// </summary>
    public Player? CurrentPlayer { get; }

    /// <summary>
    /// The active player for the current phase.
    /// </summary>
    public Player? ActivePlayer { get; }

    /// <summary>
    /// Number of consecutive passes.
    /// </summary>
    public int PassCount { get; }

    /// <summary>
    /// Total number of players in the game.
    /// </summary>
    public int TotalPlayers { get; }

    /// <summary>
    /// Whether all players have passed in succession.
    /// </summary>
    public bool AllPlayersPassed => PassCount >= TotalPlayers;

    private PriorityState(Player? currentPlayer, Player? activePlayer, int passCount, int totalPlayers)
    {
        CurrentPlayer = currentPlayer;
        ActivePlayer = activePlayer;
        PassCount = passCount;
        TotalPlayers = totalPlayers;
    }

    /// <summary>
    /// Create initial priority state for a phase.
    /// </summary>
    public static PriorityState Initial(Player activePlayer, int totalPlayers)
    {
        if (activePlayer == null)
        {
            throw new ArgumentNullException(nameof(activePlayer));
        }

        if (totalPlayers < 2)
        {
            throw new ArgumentException("Must have at least 2 players", nameof(totalPlayers));
        }

        return new PriorityState(activePlayer, activePlayer, 0, totalPlayers);
    }

    /// <summary>
    /// Create priority state with current player changed.
    /// Resets pass count (used when priority is given after an action).
    /// </summary>
    public PriorityState WithCurrentPlayer(Player? currentPlayer)
    {
        return new PriorityState(currentPlayer, ActivePlayer, 0, TotalPlayers); // Reset pass count
    }

    /// <summary>
    /// Create priority state with current player changed, keeping pass count.
    /// Used when priority naturally passes to next player.
    /// </summary>
    public PriorityState WithCurrentPlayerKeepPassCount(Player? currentPlayer)
    {
        return new PriorityState(currentPlayer, ActivePlayer, PassCount, TotalPlayers); // Keep pass count
    }

    /// <summary>
    /// Create priority state with pass count incremented.
    /// </summary>
    public PriorityState WithPassIncremented()
    {
        return new PriorityState(CurrentPlayer, ActivePlayer, PassCount + 1, TotalPlayers);
    }

    /// <summary>
    /// Create priority state with pass count reset.
    /// </summary>
    public PriorityState WithPassReset()
    {
        return new PriorityState(CurrentPlayer, ActivePlayer, 0, TotalPlayers);
    }

    /// <summary>
    /// Create reset priority state (no current player).
    /// </summary>
    public static PriorityState Reset(int totalPlayers)
    {
        return new PriorityState(null, null, 0, totalPlayers);
    }

    public bool Equals(PriorityState? other)
    {
        if (other == null) return false;
        return CurrentPlayer == other.CurrentPlayer &&
               ActivePlayer == other.ActivePlayer &&
               PassCount == other.PassCount &&
               TotalPlayers == other.TotalPlayers;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as PriorityState);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(CurrentPlayer, ActivePlayer, PassCount, TotalPlayers);
    }

    public static bool operator ==(PriorityState? left, PriorityState? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(PriorityState? left, PriorityState? right)
    {
        return !(left == right);
    }
}
