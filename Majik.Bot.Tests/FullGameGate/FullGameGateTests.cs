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
/// exactly one zone at every step boundary (CR 400.1), and no contained engine
/// fault is reported.
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

    // signature → why it is tolerated + the tracking issue.
    // 3544 = engine crashes / contained faults; 3545 = zone desync; 3543 = this gate.
    private static readonly IReadOnlyDictionary<string, Known> AllowList = new Dictionary<string, Known>
    {
        ["ZoneIntegrity|Aether Vial"] = new("in both the Stack and the Battlefield after being cast: the card stays in the Stack zone after it moves on (CR 400.1)", 3545),
        ["ZoneIntegrity|Chalice of the Void"] = new("in both the Stack and the Battlefield after being cast: the card stays in the Stack zone after it moves on (CR 400.1)", 3545),
        ["ZoneIntegrity|Claws of Gix"] = new("in both the Stack and the Battlefield after being cast: the card stays in the Stack zone after it moves on (CR 400.1)", 3545),
        ["ZoneIntegrity|Mox Opal"] = new("in both the Stack and the Battlefield after being cast: the card stays in the Stack zone after it moves on (CR 400.1)", 3545),
        ["ZoneIntegrity|Mishra's Bauble"] = new("in both the Stack and the Graveyard after being cast: the card stays in the Stack zone after it moves on (CR 400.1)", 3545),
        ["ZoneIntegrity|Tormod's Crypt"] = new("in both the Stack and the Graveyard after being cast: the card stays in the Stack zone after it moves on (CR 400.1)", 3545),
        ["ZoneIntegrity|Walking Ballista"] = new("in both the Stack and the Graveyard after being cast: the card stays in the Stack zone after it moves on (CR 400.1)", 3545),
        ["ZoneIntegrity|Welding Jar"] = new("in both the Stack and the Graveyard after being cast: the card stays in the Stack zone after it moves on (CR 400.1)", 3545),
        // Seen once in ~25 full passes (SultaiMidrange vs EsperBlink seed 1), on a
        // nondeterministically reached game path.
        ["ZoneIntegrity|Shardless Agent"] = new("intermittent: the card is found in two zones at once (CR 400.1)", 3545, Intermittent: true),
        // A live loop: state-based actions keep executing (millions of
        // StateBasedActionExecutedEvent in 60s, CR 704.3 never converges) until
        // the timeout cancels the game. Seen only in Yawg games (GoryoVengeance vs
        // Yawg seed 0, Yawg vs LivingEnd seed 1), on game paths that the
        // engine's run-to-run nondeterminism reaches in roughly a third of runs,
        // sequential or parallel. Not a crash or a zone desync; tracked on the
        // gate issue until it gets its own.
        ["Timeout|StateBasedActionExecutedEvent"] = new("intermittent state-based-action loop (hang)", 3543, Intermittent: true),
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
        await Parallel.ForEachAsync(games,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (game, _) => results.Add(await FullGameGateRunner.RunAsync(game, MaxTurns, GameTimeout)));
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

        var unlisted = bySignature.Where(g => !AllowList.ContainsKey(g.Key))
            .Select(g => $"{g.Key} ({g.Count()} games, e.g. {g.First().Game})").ToList();
        var vanished = AllowList.Where(kv => !kv.Value.Intermittent).Select(kv => kv.Key).Except(bySignature.Select(g => g.Key)).OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => $"{k} (#{AllowList[k].Issue})").ToList();

        unlisted.Should().BeEmpty("a failure signature that is not on the allow-list is a new engine bug");
        vanished.Should().BeEmpty("an allow-listed signature no longer occurs: it was fixed, remove its entry");
    }
}
