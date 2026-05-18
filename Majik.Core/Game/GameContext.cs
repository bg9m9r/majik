using Majik.Core.Players;
using Majik.Core.StateMachine;

namespace Majik.Core.Game;

/// <summary>
/// Read-only view passed to a player agent at every choice point.
/// Lets the agent inspect game state (who's active, what phase, life totals,
/// the stack) without holding a reference to mutable engine internals.
/// </summary>
public sealed class GameContext
{
    public Player Self { get; }
    public IReadOnlyList<Player> AllPlayers { get; }
    public Player ActivePlayer { get; }
    public int TurnNumber { get; }
    public PhaseStateType? CurrentPhase { get; }
    public Majik.Core.Stack.Stack Stack { get; }

    public GameContext(
        Player self,
        IReadOnlyList<Player> allPlayers,
        Player activePlayer,
        int turnNumber,
        PhaseStateType? currentPhase,
        Majik.Core.Stack.Stack stack)
    {
        Self = self ?? throw new ArgumentNullException(nameof(self));
        AllPlayers = allPlayers ?? throw new ArgumentNullException(nameof(allPlayers));
        ActivePlayer = activePlayer ?? throw new ArgumentNullException(nameof(activePlayer));
        TurnNumber = turnNumber;
        CurrentPhase = currentPhase;
        Stack = stack ?? throw new ArgumentNullException(nameof(stack));
    }
}
