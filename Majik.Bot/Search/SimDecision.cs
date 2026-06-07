namespace Majik.Bot.Search;

/// <summary>
/// Which flavor of decision the engine has asked the search seat to make.
/// </summary>
public enum SimDecisionKind
{
    DeclareAttackers,
    DeclareBlockers,
    Priority,
}

/// <summary>
/// A decision point surfaced by <see cref="SearchAgent"/> to the search loop,
/// or a terminal marker when the game ended before another decision was reached.
///
/// <para>
/// When <see cref="IsTerminal"/> is true the game is over: <see cref="LegalMoves"/>
/// is empty and <see cref="TerminalValue"/> holds the leaf evaluation score
/// from the searched seat's perspective (large positive = win, large negative
/// = loss). When <see cref="IsTerminal"/> is false a real decision is ready and
/// <see cref="LegalMoves"/> is non-empty.
/// </para>
/// </summary>
public sealed class SimDecision
{
    public SimDecisionKind Kind { get; }
    public IReadOnlyList<SimMove> LegalMoves { get; }

    /// <summary>True when the game ended before a searched decision was reached.</summary>
    public bool IsTerminal { get; }

    /// <summary>
    /// Leaf evaluation score (searched-seat POV, higher = better) when
    /// <see cref="IsTerminal"/> is true; undefined otherwise.
    /// </summary>
    public double TerminalValue { get; }

    /// <summary>Normal decision-point constructor.</summary>
    public SimDecision(SimDecisionKind kind, IReadOnlyList<SimMove> legalMoves)
    {
        Kind = kind;
        LegalMoves = legalMoves ?? throw new ArgumentNullException(nameof(legalMoves));
        IsTerminal = false;
        TerminalValue = 0.0;
    }

    /// <summary>Terminal constructor: no moves, game is over.</summary>
    public static SimDecision Terminal(double value) => new(value);

    private SimDecision(double terminalValue)
    {
        Kind = SimDecisionKind.Priority; // unused sentinel
        LegalMoves = Array.Empty<SimMove>();
        IsTerminal = true;
        TerminalValue = terminalValue;
    }
}
