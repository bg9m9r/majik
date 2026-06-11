using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Fuzz;

public class BotVsBotFuzzTests
{
    private readonly ITestOutputHelper _out;

    public BotVsBotFuzzTests(ITestOutputHelper output) => _out = output;

    // The BotDeckCatalog archetypes to fuzz. Extend as the catalog grows.
    private static readonly string[] Archetypes = { "Burn", "BorosEnergy" };
    private const int SeedsPerPairing = 5;
    private const int MaxTurns = 25;

    public static IEnumerable<object[]> DeckPairingsBySeed()
    {
        foreach (var a in Archetypes)
            foreach (var b in Archetypes)
                for (int seed = 0; seed < SeedsPerPairing; seed++)
                    yield return new object[] { a, b, seed };
    }

    [Theory]
    [MemberData(nameof(DeckPairingsBySeed))]
    public async Task Fuzz_BotVsBot_NoCrash_NoInvariantViolation(string deckA, string deckB, int seed)
    {
        var result = await FuzzGameRunner.RunOnce(
            deckA, deckB, seed, MaxTurns, System.TimeSpan.FromSeconds(60));

        result.TimedOut.Should().BeFalse($"seed {seed} {deckA} vs {deckB} hung (possible infinite loop)");

        // Soft violations (e.g. TurnCapReached) are suspicious but not necessarily bugs.
        // Surface them in test output without failing.
        var softViolations = result.Violations.Where(v => !v.IsHard).ToList();
        if (softViolations.Count > 0)
        {
            _out.WriteLine(
                $"[soft] {deckA} vs {deckB} seed {seed}: "
                + string.Join(", ", softViolations.Select(v => $"{v.Kind} T{v.Turn}/{v.Phase}")));
        }

        // Hard violations are real engine bugs — fail with a rich repro message.
        var hardViolations = result.Violations.Where(v => v.IsHard).ToList();
        hardViolations.Should().BeEmpty(
            "no hard invariant should break. Repro: FuzzGameRunner.RunOnce(\""
            + $"{deckA}\", \"{deckB}\", {seed}, {MaxTurns}, 60s)\n"
            + string.Join("\n", hardViolations.Select(v => $"  [{v.Kind}] T{v.Turn}/{v.Phase}: {v.Detail}")));
    }
}
