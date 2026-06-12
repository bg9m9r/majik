using Majik.Bot;

namespace Majik.Bot.Probes;

/// <summary>Which seat's STRATEGY won a single probe game (or a draw at the
/// turn cap / inconclusive on an engine crash). Mirrors the xUnit
/// <c>ProbeHarness.SeatAWinner</c> vocabulary.</summary>
public enum ProbeOutcome
{
    SeatA,
    SeatB,
    Draw,
    Inconclusive,
}

/// <summary>One played game inside a probe head: its index within the head,
/// the fixed seed (<c>SeedBlock + Index</c>), which physical seat (Alice)
/// hosted strategy A, and the outcome.</summary>
public sealed record ProbeGameRecord(
    int Index,
    int Seed,
    bool AIsAlice,
    ProbeOutcome Outcome);

/// <summary>
/// One head-to-head cell definition: strategy A (the seat under measurement,
/// playing <see cref="DeckA"/>) vs strategy B (the reference seat, playing
/// <see cref="DeckB"/>) over <see cref="Games"/> paired-seed games
/// (<c>SeedBlock + i</c>, alternating which physical seat hosts strategy A).
///
/// <para><see cref="StrategyA"/>/<see cref="StrategyB"/> are seed →
/// <see cref="BotConfig"/> factories, mirroring the xUnit ProbeHarness
/// convention (the deck travels with the strategy).</para>
///
/// <para><see cref="Canary"/> cells are excluded from a panel's headline mean
/// — they exist to catch instrument drift on a known-degenerate matchup, not
/// to measure strength.</para>
/// </summary>
public sealed record ProbeHead(
    string Name,
    string DeckA,
    string DeckB,
    Func<int, BotConfig> StrategyA,
    Func<int, BotConfig> StrategyB,
    int SeedBlock,
    int Games,
    int MaxTurns = 30,
    bool Canary = false,
    string SeatALabel = "mcts",
    string SeatBLabel = "frozen-fb1",
    int? Iterations = null,
    int? BudgetMs = null);
