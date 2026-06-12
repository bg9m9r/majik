using Majik.Bot;

namespace Majik.Bot.Probes;

/// <summary>
/// The FB1 frozen-baseline ladder panel — 13 cells measuring the CURRENT
/// search bot (live-shape mcts config) against the FROZEN reference opponent
/// (<c>Strategy="frozen-fb1"</c>, cut 2026-06-12):
///
/// <list type="bullet">
///   <item>5 mirror cells — one per panel archetype (Prowess, Burn,
///     AzoriusControl, SultaiMidrange, EldraziTron), same deck both seats.</item>
///   <item>4 asymmetric pairs × both seat assignments (8 cells) —
///     AzoriusControl/Burn, SultaiMidrange/EldraziTron, Prowess/SultaiMidrange,
///     and Prowess/Burn. The Prowess/Burn pair is the CANARY (measured
///     ~3.3% intrinsic Prowess-seat win-rate — a known near-auto-loss):
///     both its cells are excluded from the headline mean and exist only to
///     catch instrument drift.</item>
/// </list>
///
/// Seed blocks: <c>90000 + 1000 × cellIndex</c> — verified disjoint from
/// every xUnit ProbeHarness family block (5000/7000/20000/30000/40000/
/// 50000/60000/70000/80000).
/// </summary>
public static class LadderPanel
{
    /// <summary>Games per cell for the full baseline ritual.</summary>
    public const int DefaultGames = 30;

    /// <summary>Max turns per game (draw past this) — harness convention.</summary>
    public const int MaxTurns = 30;

    /// <summary>Base of the panel's seed-block range; cell i uses
    /// <c>PanelBaseSeed + 1000 × i</c>. No collision with the xUnit harness
    /// families (largest existing block: 80000).</summary>
    public const int PanelBaseSeed = 90000;

    // ── The live production search shape (the seat under measurement) ──────
    // Mirrors the shipped prod regime: reuse ON, iteration cap 800, 1500 ms
    // wall-clock, honest inference (no peek, no assumed deck), default
    // per-world split (400 ms → K = round(1500/400) = 4 worlds).

    /// <summary>Live iteration cap (echoed into results).</summary>
    public const int LiveIterations = 800;

    /// <summary>Live wall-clock budget per search (ms) (echoed into results).</summary>
    public const int LiveBudgetMs = 1500;

    /// <summary>Derived determinized world count at the live budget split
    /// (1500 ms / 400 ms-per-world default = 4) — config echo only.</summary>
    public const int LiveWorlds = 4;

    /// <summary>Live-shape mcts config for the searched seat: honest
    /// inference, tree-state reuse ON, cap 800 / 1500 ms. SearchConcurrency
    /// stays null — panel cells run in parallel and must not gate each other
    /// (the process-wide gate is a 1-vCPU prod concern, not a probe one).</summary>
    public static BotConfig Searched(string archetype, int seed) => new(
        archetype, Strategy: "mcts",
        RandomSeed: seed,
        MaxMctsIterations: LiveIterations,
        MaxMctsBudgetMs: LiveBudgetMs,
        PrioritySearchEnabled: true,
        OpponentArchetype: null,
        InferOpponentArchetype: true,
        TreeStateReuse: true);

    /// <summary>The frozen reference seat: FB1 plays heuristically — no
    /// search, no budget sensitivity.</summary>
    public static BotConfig Frozen(string archetype, int seed) => new(
        archetype, Strategy: "frozen-fb1", RandomSeed: seed);

    /// <summary>The five panel archetypes.</summary>
    public static readonly IReadOnlyList<string> Archetypes = new[]
    {
        "Prowess", "Burn", "AzoriusControl", "SultaiMidrange", "EldraziTron",
    };

    /// <summary>The 13-cell FB1 panel (see class docs).</summary>
    public static IReadOnlyList<ProbeHead> FB1 { get; } = Build();

    /// <summary>Resolve one head by name (case-insensitive), or null.</summary>
    public static ProbeHead? Find(string name) =>
        FB1.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<ProbeHead> Build()
    {
        var heads = new List<ProbeHead>();

        // 5 mirrors: searched seat vs FB1, same deck.
        foreach (var archetype in Archetypes)
        {
            heads.Add(Cell($"mirror-{archetype.ToLowerInvariant()}", archetype, archetype, heads.Count));
        }

        // 4 asym pairs × both seat assignments. The searched seat (A) always
        // runs the live-shape mcts config; swapping the PAIR swaps which deck
        // it pilots vs FB1 on the other deck.
        AddPair(heads, "AzoriusControl", "Burn");
        AddPair(heads, "SultaiMidrange", "EldraziTron");
        AddPair(heads, "Prowess", "SultaiMidrange");
        AddPair(heads, "Prowess", "Burn", canary: true); // known near-auto-loss pair

        return heads;
    }

    private static void AddPair(List<ProbeHead> heads, string deckX, string deckY, bool canary = false)
    {
        heads.Add(Cell($"asym-{deckX.ToLowerInvariant()}-vs-{deckY.ToLowerInvariant()}", deckX, deckY, heads.Count, canary));
        heads.Add(Cell($"asym-{deckY.ToLowerInvariant()}-vs-{deckX.ToLowerInvariant()}", deckY, deckX, heads.Count, canary));
    }

    private static ProbeHead Cell(string name, string deckA, string deckB, int cellIndex, bool canary = false) => new(
        Name: name,
        DeckA: deckA,
        DeckB: deckB,
        StrategyA: seed => Searched(deckA, seed),
        StrategyB: seed => Frozen(deckB, seed),
        SeedBlock: PanelBaseSeed + 1000 * cellIndex,
        Games: DefaultGames,
        MaxTurns: MaxTurns,
        Canary: canary,
        SeatALabel: "mcts(live)",
        SeatBLabel: "frozen-fb1",
        Iterations: LiveIterations,
        BudgetMs: LiveBudgetMs);
}
