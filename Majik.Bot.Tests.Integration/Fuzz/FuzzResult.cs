using System.Collections.Generic;

namespace Majik.Bot.Tests.Integration.Fuzz;

/// <summary>Outcome of a single seeded bot-vs-bot fuzz game.</summary>
public sealed record FuzzResult(
    int Seed,
    string DeckA,
    string DeckB,
    int Turns,
    string? Winner,
    bool TimedOut,
    bool ReachedTurnCap,
    IReadOnlyList<InvariantViolation> Violations);
