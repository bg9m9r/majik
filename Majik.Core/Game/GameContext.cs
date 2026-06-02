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

    /// <summary>
    /// CR 305.2 — whether <see cref="Self"/> may play a land right now (own
    /// turn, a main phase, empty stack, and the per-turn land-drop cap not yet
    /// reached). The priority loop computes this from the live
    /// <c>LandDropTracker</c> so consumers (<c>PriorityKinds</c>, the auto-pass
    /// gate, bot agents) don't propose a land the engine will only reject —
    /// which previously flooded logs and spun the priority round. Defaults to
    /// <c>true</c> for contexts built outside the priority loop (which don't
    /// gate land plays on this field).
    /// </summary>
    public bool LandPlayAvailable { get; }

    public GameContext(
        Player self,
        IReadOnlyList<Player> allPlayers,
        Player activePlayer,
        int turnNumber,
        PhaseStateType? currentPhase,
        Majik.Core.Stack.Stack stack,
        bool landPlayAvailable = true)
    {
        Self = self ?? throw new ArgumentNullException(nameof(self));
        AllPlayers = allPlayers ?? throw new ArgumentNullException(nameof(allPlayers));
        ActivePlayer = activePlayer ?? throw new ArgumentNullException(nameof(activePlayer));
        TurnNumber = turnNumber;
        CurrentPhase = currentPhase;
        Stack = stack ?? throw new ArgumentNullException(nameof(stack));
        LandPlayAvailable = landPlayAvailable;
    }
}
