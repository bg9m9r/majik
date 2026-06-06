namespace Majik.Bot.Tests.Integration.Fuzz;

/// <summary>One detected breach of an engine invariant during a fuzz game.</summary>
public sealed record InvariantViolation(
    string Kind,        // e.g. "ZoneIntegrity", "SingleResult", "OrphanedTrigger"
    string Detail,      // human-readable specifics, including card/ability names
    int Turn,           // turn number when detected (0 if unknown)
    string Phase);      // phase/step name when detected ("" if unknown)
