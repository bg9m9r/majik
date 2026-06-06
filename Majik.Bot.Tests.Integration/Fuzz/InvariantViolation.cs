using System.Collections.Generic;

namespace Majik.Bot.Tests.Integration.Fuzz;

/// <summary>One detected breach of an engine invariant during a fuzz game.</summary>
public sealed record InvariantViolation(
    string Kind,        // e.g. "ZoneIntegrity", "SingleResult", "OrphanedTrigger"
    string Detail,      // human-readable specifics, including card/ability names
    int Turn,           // turn number when detected (0 if unknown)
    string Phase)       // phase/step name when detected ("" if unknown)
{
    /// <summary>
    /// Violation kinds that are suspicious but not necessarily bugs — they do not
    /// hard-fail the fuzz Theory.  All other kinds are hard failures.
    /// </summary>
    public static readonly IReadOnlySet<string> SoftKinds =
        new HashSet<string> { "TurnCapReached" };

    /// <summary>True when this violation should hard-fail the test.</summary>
    public bool IsHard => !SoftKinds.Contains(Kind);
}
