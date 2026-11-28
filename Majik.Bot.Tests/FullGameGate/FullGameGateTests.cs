using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.FullGameGate;

/// <summary>
/// Full-game gate (issue 3543). Every ordered pair of BotDeckCatalog
/// archetypes × 2 seeds, the heuristic bot on both seats, turn cap 30, run in
/// parallel shards. Per game: the game task does not fault, every card is in
/// exactly one zone at every step boundary (CR 400.1), no contained engine
/// fault is reported, and no event handler throws.
///
/// <para>Known failures are allow-listed by signature, each with a reason and
/// an issue. The gate fails on a signature that is NOT listed (a new bug) and
/// on a listed signature that no longer occurs (fixed: remove the entry, so the
/// list cannot go stale and hide a regression).</para>
/// </summary>
[Trait("Category", "FullGameGate")]
public class FullGameGateTests
{
    private const int MaxTurns = 30;
    private static readonly int[] Seeds = { 0, 1 };
    private static readonly TimeSpan GameTimeout = TimeSpan.FromSeconds(20);

    /// <param name="Intermittent">The failure does not occur on every run, so
    /// its absence does not mean it was fixed: exempt from the vanished check
    /// (a NEW signature still fails). Use only for a proven-intermittent bug.</param>
    private sealed record Known(string Reason, int Issue, bool Intermittent = false);

    private const string StaysOnStack = "the card stays in the Stack zone after it moves on (CR 400.1)";

    // signature → why it is tolerated + the tracking issue.
    // 3544 = engine crashes / contained faults; 3545 = zone desync; 3543 = this gate.
    private static readonly IReadOnlyDictionary<string, Known> AllowList = new Dictionary<string, Known>
    {
        // Derived from ten Release runs at the Part 2 tip; every entry below
        // was observed, and each not marked Intermittent was seen in every run.
        // The card stays in the Stack zone after it moves on
        // (CR 400.1): to the Battlefield when it resolves; to the Graveyard,
        // then Exile, as it is sacrificed and its card exiled (Ballista,
        // Bauble, Jar, all in EldraziTron games).
        ["ZoneIntegrity|Chalice of the Void|Battlefield+Stack"] = new(StaysOnStack, 3545),
        ["ZoneIntegrity|Claws of Gix|Battlefield+Stack"] = new(StaysOnStack, 3545),
        ["ZoneIntegrity|Mox Opal|Battlefield+Stack"] = new(StaysOnStack, 3545),
        ["ZoneIntegrity|Mox Opal|Graveyard+Stack"] = new(StaysOnStack, 3545),
        ["ZoneIntegrity|Mishra's Bauble|Exile+Stack"] = new(StaysOnStack, 3545),
        ["ZoneIntegrity|Mishra's Bauble|Graveyard+Stack"] = new(StaysOnStack, 3545),
        ["ZoneIntegrity|Tormod's Crypt|Graveyard+Stack"] = new(StaysOnStack, 3545),
        ["ZoneIntegrity|Walking Ballista|Exile+Stack"] = new(StaysOnStack, 3545),
        ["ZoneIntegrity|Walking Ballista|Graveyard+Stack"] = new(StaysOnStack, 3545),
        ["ZoneIntegrity|Welding Jar|Exile+Stack"] = new(StaysOnStack, 3545),
        ["ZoneIntegrity|Welding Jar|Graveyard+Stack"] = new(StaysOnStack, 3545),
        // The same leftover, on a path where the card is then shuffled into its
        // library: 1 of 10 runs (EldraziTron vs SultaiMidrange seed 1).
        ["ZoneIntegrity|Walking Ballista|Library+Stack"] = new("intermittent: " + StaysOnStack, 3545, Intermittent: true),
        // Not a Stack leftover: a second desync the name-only signature hid
        // (10/10 runs, SultaiMidrange vs Affinity seed 0).
        ["ZoneIntegrity|Mox Opal|Graveyard+Library"] = new("in both the Graveyard and the Library (CR 400.1)", 3545),
        // 3 of 9 runs (Yawg vs Affinity seed 1).
        ["ZoneIntegrity|Engineered Explosives|Exile+Graveyard"] = new("intermittent: in both the Graveyard and Exile (CR 400.1)", 3545, Intermittent: true),
        // No Timeout or Draw|LoopSafetyNet entry: none occurs. The old
        // Timeout|StateBasedActionExecutedEvent hang (Yawg games) was a Young
        // Wolf / Walking Ballista loop on damage a permanent kept across zones,
        // fixed by clearing marked damage on battlefield entry (CR 704.5g);
        // a future runaway ends as a Draw|LoopSafetyNet signature, which fails
        // the gate as new.
    };

    private readonly ITestOutputHelper _out;
    public FullGameGateTests(ITestOutputHelper output) => _out = output;

    public static IReadOnlyList<GateGame> AllGames()
    {
        var archetypes = Majik.Bot.Decks.BotDeckCatalog.Archetypes.OrderBy(a => a, StringComparer.Ordinal).ToList();
        return (from a in archetypes from b in archetypes from s in Seeds select new GateGame(a, b, s)).ToList();
    }

    [Fact]
    [ExpectsContainedFault] // the gate accounts for contained faults itself, against the allow-list
    public async Task EveryArchetypePairing_PlaysToCompletion_WithoutNewFaultsOrZoneDesyncs()
    {
        var games = AllGames();
        var results = new ConcurrentBag<GateGameResult>();
        var wall = Stopwatch.StartNew();
        using (FullGameGateRunner.CaptureHandlerFaults())
        {
            await Parallel.ForEachAsync(games,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                async (game, _) => results.Add(await FullGameGateRunner.RunAsync(game, MaxTurns, GameTimeout)));
        }
        wall.Stop();

        var times = results.Select(r => r.Elapsed.TotalMilliseconds).OrderBy(t => t).ToList();
        double P(double q) => times[Math.Min(times.Count - 1, (int)(q * times.Count))];
        _out.WriteLine($"GATE games={games.Count} shards={Environment.ProcessorCount} wall={wall.Elapsed.TotalSeconds:F1}s " +
                       $"p50={P(0.50):F1}ms p95={P(0.95):F1}ms p99={P(0.99):F1}ms max={times[^1]:F0}ms");
        var slowest = results.OrderByDescending(r => r.Elapsed).First();
        _out.WriteLine($"GATE slowest: {slowest.Game} {slowest.Elapsed.TotalMilliseconds:F0}ms turns={slowest.Turns}");

        var bySignature = results
            .SelectMany(r => r.Signatures.Select(s => (Signature: s.Key, Detail: s.Value, r.Game)))
            .GroupBy(x => x.Signature)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        foreach (var g in bySignature)
        {
            var tag = AllowList.TryGetValue(g.Key, out var k) ? $"known #{k.Issue}" : "NEW";
            var eg = g.OrderBy(x => x.Game.ToString(), StringComparer.Ordinal).First();
            _out.WriteLine($"GATE sig [{tag}] {g.Count()}x {g.Key}  e.g. {eg.Game} ({eg.Detail})");
        }

        var (unlisted, vanishedKeys) = FullGameGateRunner.Evaluate(results,
            AllowList.ToDictionary(kv => kv.Key, kv => kv.Value.Intermittent));
        var vanished = vanishedKeys.Select(k => $"{k} (#{AllowList[k].Issue})").ToList();

        unlisted.Should().BeEmpty("a failure signature that is not on the allow-list is a new engine bug");
        vanished.Should().BeEmpty("an allow-listed signature no longer occurs: it was fixed, remove its entry");
    }
}
