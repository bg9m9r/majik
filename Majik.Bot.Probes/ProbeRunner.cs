using Majik.Bot;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Random;

namespace Majik.Bot.Probes;

/// <summary>
/// Runs probe heads: one head sequentially (paired seeds, alternating first
/// seat), a panel of heads concurrently. Liveness-only by contract — the
/// runner never asserts win-rate thresholds; the operator judges from the
/// results (interpretation contract unchanged from the xUnit probes).
///
/// <para>Game-loop mechanics ported from the xUnit
/// <c>Majik.Bot.Tests.Integration.Search.ProbeHarness</c>: real typed deck
/// shells from the embedded seed, per-seat tie-break seeds (B = A + 500),
/// per-game 6-minute cap, a crashed game counts Inconclusive and never aborts
/// the head.</para>
/// </summary>
public static class ProbeRunner
{
    /// <summary>Embedded card repository — one shared instance for all heads
    /// (lazy gz-seed load; reads are thread-safe).</summary>
    private static readonly EmbeddedCardRepository Repo = new();

    /// <summary>Per-game wall-clock cap. Generous relative to an
    /// iteration-bound search so CPU contention from concurrent panel cells
    /// cannot trip it (harness convention).</summary>
    private static readonly TimeSpan PerGameCap = TimeSpan.FromMinutes(6);

    /// <summary>
    /// Run one head: <see cref="ProbeHead.Games"/> games in order, game i on
    /// seed <c>SeedBlock + i</c>, alternating which physical seat (Alice)
    /// hosts strategy A to cancel play/draw bias. Per-game lines stream to
    /// <paramref name="progress"/> AND the shared <see cref="ProbeProgress"/>
    /// log path. A single crashed game counts Inconclusive — it cannot abort
    /// the head.
    /// </summary>
    public static async Task<ProbeResult> RunAsync(
        ProbeHead head,
        Action<string>? progress = null,
        CancellationToken ct = default)
    {
        int aWins = 0, bWins = 0, draws = 0, inconclusive = 0;
        var games = new List<ProbeGameRecord>(head.Games);

        for (int i = 0; i < head.Games; i++)
        {
            ct.ThrowIfCancellationRequested();

            // Alternate which physical seat (Alice) hosts strategy A so neither
            // the play nor the draw is systematically assigned to one strategy.
            bool aIsAlice = i % 2 == 0;
            int seed = head.SeedBlock + i;

            var outcome = await PlayOneGame(head, aIsAlice, seed, i, Emit, ct);

            switch (outcome)
            {
                case ProbeOutcome.SeatA:        aWins++;        break;
                case ProbeOutcome.SeatB:        bWins++;        break;
                case ProbeOutcome.Draw:         draws++;        break;
                case ProbeOutcome.Inconclusive: inconclusive++; break;
            }

            games.Add(new ProbeGameRecord(i, seed, aIsAlice, outcome));

            Emit(
                $"  [{head.Name}] game {i,2}: seed={seed} A={(aIsAlice ? "Alice" : "Bob")} " +
                $"result={outcome}  cumulative: A {aWins} B {bWins} draw {draws} inconclusive {inconclusive}");
        }

        int decided = aWins + bWins;
        double winRate = decided > 0 ? (double)aWins / decided : 0.0;
        Emit(
            $"[STRENGTH] [{head.Name}] A {aWins}/{decided} decided " +
            $"({head.Games} played, {draws} draws, {inconclusive} inconclusive) win-rate={winRate:P1}");

        return new ProbeResult(
            HeadName: head.Name,
            DeckA: head.DeckA,
            DeckB: head.DeckB,
            SeatALabel: head.SeatALabel,
            SeatBLabel: head.SeatBLabel,
            Canary: head.Canary,
            SeedBlock: head.SeedBlock,
            GamesPlayed: head.Games,
            AWins: aWins,
            BWins: bWins,
            Draws: draws,
            Inconclusive: inconclusive,
            Games: games,
            Iterations: head.Iterations,
            BudgetMs: head.BudgetMs);

        void Emit(string line)
        {
            progress?.Invoke(line);
            ProbeProgress.Log(line);
        }
    }

    /// <summary>
    /// Run a panel of heads concurrently (each head's games stay sequential),
    /// capped at <paramref name="maxConcurrency"/> (default
    /// <c>min(heads, processorCount / 2)</c>, floor 1). Cell order in the
    /// result matches the input order regardless of completion order.
    /// </summary>
    public static async Task<PanelResult> RunPanelAsync(
        IReadOnlyList<ProbeHead> heads,
        int? maxConcurrency = null,
        Action<string>? progress = null,
        string? commitHash = null,
        CancellationToken ct = default)
    {
        int cap = Math.Max(1, maxConcurrency
            ?? Math.Min(heads.Count, Environment.ProcessorCount / 2));

        using var gate = new SemaphoreSlim(cap, cap);
        var tasks = heads.Select(async head =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await RunAsync(head, progress, ct);
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        var cells = await Task.WhenAll(tasks);
        return new PanelResult(cells, commitHash, DateTime.UtcNow);
    }

    /// <summary>
    /// Run one game of seat-A-strategy (deck <c>head.DeckA</c>) vs
    /// seat-B-strategy (deck <c>head.DeckB</c>). Decks are materialized as
    /// real typed shells from the embedded seed (abilities bound by
    /// <see cref="GameFacade.Create"/> through the production binder chain);
    /// each seat's DECK travels with its strategy when seat A hosts Bob.
    /// Per-seat tie-break seeds differ (B = seed + 500, harness convention).
    /// </summary>
    private static async Task<ProbeOutcome> PlayOneGame(
        ProbeHead head,
        bool aIsAlice,
        int seed,
        int gameIndex,
        Action<string> emit,
        CancellationToken ct)
    {
        string aliceName = aIsAlice ? "A" : "B";
        string bobName   = aIsAlice ? "B" : "A";
        string aliceDeck = aIsAlice ? head.DeckA : head.DeckB;
        string bobDeck   = aIsAlice ? head.DeckB : head.DeckA;

        var facade = GameFacade.Create(
            aliceName: aliceName,
            bobName:   bobName,
            aliceDeck: LoadRealDeck(aliceDeck),
            bobDeck:   LoadRealDeck(bobDeck),
            cardRepo:  Repo);

        var aCfg = head.StrategyA(seed);
        var bCfg = head.StrategyB(seed + 500);

        if (aIsAlice)
        {
            facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, aCfg));
            facade.ReplaceBobAgent(  new BotPlayerAgent(facade.Bob,   bCfg));
        }
        else
        {
            facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, bCfg));
            facade.ReplaceBobAgent(  new BotPlayerAgent(facade.Bob,   aCfg));
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(PerGameCap);

        try
        {
            await facade.StartFullGameAsync(
                maxTurns: head.MaxTurns,
                ct: cts.Token,
                rng: new GameRandom(seed));

            var result = await facade.FullGameTask!;

            if (result.Winner == null)
                return ProbeOutcome.Draw;

            bool aWon = aIsAlice
                ? ReferenceEquals(result.Winner, facade.Alice)
                : ReferenceEquals(result.Winner, facade.Bob);

            return aWon ? ProbeOutcome.SeatA : ProbeOutcome.SeatB;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // One crash (or per-game timeout) must not abort the whole head.
            emit($"  [{head.Name}] game {gameIndex,2}: INCONCLUSIVE — {ex.GetType().Name}: {ex.Message}");
            return ProbeOutcome.Inconclusive;
        }
    }

    /// <summary>
    /// Materialize an archetype's deck list into REAL typed shells resolved
    /// from the embedded seed (same shape the server's RealDeckLoader and the
    /// xUnit harness's <c>DeckLoader.LoadReal</c> produce). Throws if a name
    /// is absent from the seed — a real regression, not something to paper
    /// over with a vanilla fallback.
    /// </summary>
    private static IReadOnlyList<ICard> LoadRealDeck(string archetype)
    {
        var names = Majik.Bot.Decks.BotDeckCatalog.Get(archetype);
        return names.Select(n =>
        {
            var entity = Repo.GetByName(n)
                ?? throw new InvalidOperationException($"bot-deck card not in embedded seed: '{n}'");
            return DeckCardShellBuilder.Build(entity);
        }).ToList();
    }
}
