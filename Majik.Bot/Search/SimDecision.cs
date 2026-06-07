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
/// A decision point surfaced by <see cref="SearchAgent"/> to the search loop.
/// Contains the decision kind and all legal moves the engine offered.
/// </summary>
public sealed class SimDecision
{
    public SimDecisionKind Kind { get; }
    public IReadOnlyList<SimMove> LegalMoves { get; }

    public SimDecision(SimDecisionKind kind, IReadOnlyList<SimMove> legalMoves)
    {
        Kind = kind;
        LegalMoves = legalMoves ?? throw new ArgumentNullException(nameof(legalMoves));
    }
}
