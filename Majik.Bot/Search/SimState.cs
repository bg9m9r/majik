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

    /// <summary>
    /// The opponent's full decklist (name strings) used to seed determinization
    /// of hidden zones. <c>null</c> means perfect-info search (no resampling).
    /// </summary>
    public IReadOnlyList<string>? OpponentDecklist { get; }

    /// <summary>
    /// The deterministic world seed for hidden-zone resampling. <c>null</c> means
    /// perfect-info search (no resampling). When set together with
    /// <see cref="OpponentDecklist"/>, each sandbox clone is resampled before it
    /// is searched / rolled out.
    /// </summary>
    public int? WorldSeed { get; }

    /// <summary>
    /// Per-world public observations (e.g. opponent cards already revealed on the
    /// battlefield / in the graveyard) threaded to the resampler so the sampled
    /// hidden zones are made consistent with what has been observed. <c>null</c>
    /// means no observation augmentation (the perfect-info / unaugmented path).
    /// </summary>
    public IReadOnlyList<string>? ObservedPublic { get; }

    /// <summary>
    /// Cache of this world's MATERIALIZED base: the live players cloned once and
    /// resampled with REAL prod-built cards (<c>DeckCardBuilder</c>), set lazily by
    /// <see cref="EngineSimulator"/>'s <c>ResolveCloneSource</c> on the first
    /// determinized Advance / Rollout. Every subsequent per-sim sandbox clones FROM
    /// this base instead of shell-resampling per clone. Always <c>null</c> for
    /// perfect-info roots (no WorldSeed / decklist).
    ///
    /// <para><b>Mutability + threading contract:</b> deliberately a MUTABLE field
    /// (not a get-only prop) so the simulator can set it once after construction.
    /// <c>Mcts</c> is single-threaded per search and each determinized world gets
    /// its OWN SimState instance (via <see cref="WithWorldSeed"/> /
    /// <see cref="WithDeterminization"/>), so no synchronization is needed: all
    /// reads/writes of this field happen on that world's single search thread.</para>
    ///
    /// <para><b>NOT preserved by any copy helper by design</b> — a new world copy
    /// (new seed) must re-materialize its own base.</para>
    /// </summary>
    internal IReadOnlyList<Player>? MaterializedWorldPlayers;

    private SimState(
        IReadOnlyList<Player> livePlayers,
        Player activePlayer,
        int turnNumber,
        PhaseStateType phase,
        Player searchedSeat,
        IReadOnlyList<string>? opponentDecklist = null,
        int? worldSeed = null,
        IReadOnlyList<string>? observedPublic = null)
    {
        LivePlayers = livePlayers;
        ActivePlayer = activePlayer;
        TurnNumber = turnNumber;
        Phase = phase;
        SearchedSeat = searchedSeat;
        OpponentDecklist = opponentDecklist;
        WorldSeed = worldSeed;
        ObservedPublic = observedPublic;
    }

    /// <summary>
    /// Returns a copy of this state opted into determinization: the opponent's
    /// decklist and a fixed world seed are attached so each sandbox clone is
    /// resampled (hidden zones re-drawn) before it is searched. Every other field
    /// is preserved unchanged.
    /// </summary>
    public SimState WithDeterminization(IReadOnlyList<string> opponentDecklist, int worldSeed) =>
        WithDeterminization(opponentDecklist, observedPublic: null, worldSeed);

    /// <summary>
    /// As <see cref="WithDeterminization(IReadOnlyList{string}, int)"/> but also attaches
    /// a per-world <see cref="ObservedPublic"/> list, threaded to the resampler so the
    /// sampled hidden zones are made consistent with the observed public cards. Every
    /// other field is preserved unchanged.
    /// </summary>
    public SimState WithDeterminization(
        IReadOnlyList<string> opponentDecklist, IReadOnlyList<string>? observedPublic, int worldSeed)
    {
        ArgumentNullException.ThrowIfNull(opponentDecklist);
        // preserves: LivePlayers, ActivePlayer, TurnNumber, Phase, SearchedSeat
        // NOT preserved by design: MaterializedWorldPlayers (world cache — a new world copy must re-materialize)
        return new SimState(
            livePlayers: LivePlayers,
            activePlayer: ActivePlayer,
            turnNumber: TurnNumber,
            phase: Phase,
            searchedSeat: SearchedSeat,
            opponentDecklist: opponentDecklist,
            worldSeed: worldSeed,
            observedPublic: observedPublic);
    }

    /// <summary>
    /// Returns a copy of this state with a new <see cref="WorldSeed"/>, preserving
    /// the existing <see cref="OpponentDecklist"/> (used by the K-world search loop
    /// to spin the same root across many determinized worlds). Every other field is
    /// preserved unchanged.
    ///
    /// <para>
    /// Presupposes a decklist is already attached (intended use: re-seeding an
    /// already-determinized root — i.e. one produced by <see cref="WithDeterminization"/>).
    /// Calling this on a perfect-info root yields a seed-without-decklist state,
    /// which resamples nothing (see <see cref="EngineSimulator"/>'s
    /// resample-when-both-set guard); use <see cref="WithDeterminization"/> to opt in.
    /// </para>
    /// </summary>
    public SimState WithWorldSeed(int worldSeed)
    {
        // preserves: LivePlayers, ActivePlayer, TurnNumber, Phase, SearchedSeat, OpponentDecklist, ObservedPublic
        // NOT preserved by design: MaterializedWorldPlayers (world cache — a new world copy must re-materialize)
        return new SimState(
            livePlayers: LivePlayers,
            activePlayer: ActivePlayer,
            turnNumber: TurnNumber,
            phase: Phase,
            searchedSeat: SearchedSeat,
            opponentDecklist: OpponentDecklist,
            worldSeed: worldSeed,
            observedPublic: ObservedPublic);
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
