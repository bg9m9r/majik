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
    public StepStateType? CurrentPhase { get; }
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

    /// <summary>
    /// The live per-turn event tally (<see cref="Game.TurnState"/>) — creatures
    /// died this turn, attackers declared this turn, lands entered, etc. Exposed
    /// so resolution-time effects can read dynamic counts off the context (CR
    /// 700.6 per-turn tallies) — e.g. dynamic-X connive (X = creatures died /
    /// attackers declared) — instead of capturing a <c>TurnState</c> by reference
    /// at factory-build time. Null on contexts built outside a live turn (some
    /// test harnesses, the mulligan / opening-hand windows); consumers must
    /// null-guard. The live <see cref="TurnDriver"/> threads its owned TurnState
    /// in via the priority loop so <c>rc.Game.TurnState</c> is non-null at
    /// resolution in real games.
    /// </summary>
    public TurnState? TurnState { get; }

    // Memoized opponent set. The accessor sits on the MCTS legality hot path
    // (LegalActionEnumerator enumerates it per candidate action), so the prior
    // .Where(...).ToList() allocated a fresh list on every call. AllPlayers is
    // fixed at construction (immutable IReadOnlyList field); the only mutation
    // that can change the result underneath a live context is a player leaving
    // the game (HasLost flipping true — monotonic, never un-set; see
    // Player.MarkLost / the SBA loss loop). We therefore memoize the filtered
    // list keyed on the count of lost players across AllPlayers: that count is
    // monotonic non-decreasing and changes exactly when the opponent set must be
    // recomputed (CR 102.4 / 800.4 — a player who leaves is no longer an
    // opponent). Cheap O(n) validity check, no per-call allocation on the steady
    // state. Single-threaded engine — a GameContext is read by one agent at a
    // time, so no locking is needed.
    private IReadOnlyList<Player>? _opponentsCache;
    private int _opponentsCacheLostCount = -1;

    /// <summary>
    /// CR 102.1 / 102.4 — every other player still in the game (not
    /// <see cref="Self"/> and not <see cref="Players.Player.HasLost"/>). A player
    /// is never their own opponent, and a player who has left the game is no
    /// longer an opponent. Used by context-aware activation predicates and
    /// effects that need the opponent set at the choice point without a captured
    /// resolver. Memoized (invalidated when a player leaves the game) because it
    /// is enumerated on the MCTS legality hot path.
    /// </summary>
    public IReadOnlyList<Player> Opponents
    {
        get
        {
            int lostCount = 0;
            for (int i = 0; i < AllPlayers.Count; i++)
            {
                if (AllPlayers[i].HasLost)
                {
                    lostCount++;
                }
            }

            if (_opponentsCache is not null && lostCount == _opponentsCacheLostCount)
            {
                return _opponentsCache;
            }

            var opponents = new List<Player>(AllPlayers.Count);
            for (int i = 0; i < AllPlayers.Count; i++)
            {
                var p = AllPlayers[i];
                if (!ReferenceEquals(p, Self) && !p.HasLost)
                {
                    opponents.Add(p);
                }
            }

            _opponentsCache = opponents;
            _opponentsCacheLostCount = lostCount;
            return opponents;
        }
    }

    public GameContext(
        Player self,
        IReadOnlyList<Player> allPlayers,
        Player activePlayer,
        int turnNumber,
        StepStateType? currentPhase,
        Majik.Core.Stack.Stack stack,
        bool landPlayAvailable = true)
        : this(self, allPlayers, activePlayer, turnNumber, currentPhase, stack, landPlayAvailable, turnState: null)
    {
    }

    /// <summary>
    /// Overload that additionally threads the live <see cref="Game.TurnState"/>
    /// through (so resolution-time effects can read per-turn tallies). The
    /// existing 6/7-arg ctor (used widely) delegates here with a null TurnState
    /// to stay source-compatible — adding TurnState must NOT break any existing
    /// call site.
    /// </summary>
    public GameContext(
        Player self,
        IReadOnlyList<Player> allPlayers,
        Player activePlayer,
        int turnNumber,
        StepStateType? currentPhase,
        Majik.Core.Stack.Stack stack,
        bool landPlayAvailable,
        TurnState? turnState)
    {
        Self = self ?? throw new ArgumentNullException(nameof(self));
        AllPlayers = allPlayers ?? throw new ArgumentNullException(nameof(allPlayers));
        ActivePlayer = activePlayer ?? throw new ArgumentNullException(nameof(activePlayer));
        TurnNumber = turnNumber;
        CurrentPhase = currentPhase;
        Stack = stack ?? throw new ArgumentNullException(nameof(stack));
        LandPlayAvailable = landPlayAvailable;
        TurnState = turnState;
    }
}
