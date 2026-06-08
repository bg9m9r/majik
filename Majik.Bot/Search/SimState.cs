using Majik.Core.Players;
using Majik.Core.StateMachine;

namespace Majik.Bot.Search;

/// <summary>
/// A captured snapshot of a live game position used as the root of a search.
/// Stores the live (uncloned) players and the context needed to resume a
/// sandbox from this position. Cloning happens inside
/// <see cref="EngineSimulator"/> per call so that every simulation starts
/// from an independent copy of the position — the snapshot itself is
/// read-only and shared safely across calls.
///
/// <para>
/// Build via <see cref="Capture"/>; the live players are stored by reference
/// (not cloned here) and are passed directly to
/// <see cref="Majik.Core.Simulation.SandboxGame.From"/> which clones them
/// before any mutation.
/// </para>
/// </summary>
public sealed class SimState
{
    /// <summary>Live player list at the capture point (not cloned here).</summary>
    public IReadOnlyList<Player> LivePlayers { get; }

    /// <summary>The player whose decisions are being searched.</summary>
    public Player SearchedSeat { get; }

    /// <summary>Identity of the searched seat, stable across clones.</summary>
    public Guid SearchedSeatId => SearchedSeat.Id;

    /// <summary>Turn number at the capture point.</summary>
    public int TurnNumber { get; }

    /// <summary>Phase at which to resume the sandbox.</summary>
    public PhaseStateType Phase { get; }

    /// <summary>The active player at the capture point.</summary>
    public Player ActivePlayer { get; }

    private SimState(
        IReadOnlyList<Player> livePlayers,
        Player activePlayer,
        int turnNumber,
        PhaseStateType phase,
        Player searchedSeat)
    {
        LivePlayers = livePlayers;
        ActivePlayer = activePlayer;
        TurnNumber = turnNumber;
        Phase = phase;
        SearchedSeat = searchedSeat;
    }

    /// <summary>
    /// Capture the current game position as a search root.
    /// </summary>
    /// <param name="livePlayers">All players in turn order.</param>
    /// <param name="activePlayer">The player whose turn it currently is.</param>
    /// <param name="turnNumber">Current turn number.</param>
    /// <param name="phase">Phase from which to resume each sandbox run.</param>
    /// <param name="searchedSeat">The player whose decisions the search controls.</param>
    public static SimState Capture(
        IReadOnlyList<Player> livePlayers,
        Player activePlayer,
        int turnNumber,
        PhaseStateType phase,
        Player searchedSeat)
    {
        ArgumentNullException.ThrowIfNull(livePlayers);
        ArgumentNullException.ThrowIfNull(activePlayer);
        ArgumentNullException.ThrowIfNull(searchedSeat);
        if (turnNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(turnNumber), "Turn number must be >= 1.");

        return new SimState(
            livePlayers: livePlayers,
            activePlayer: activePlayer,
            turnNumber: turnNumber,
            phase: phase,
            searchedSeat: searchedSeat);
    }
}
