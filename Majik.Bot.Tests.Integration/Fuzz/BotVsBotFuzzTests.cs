using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Majik.Bot.Tests.Integration.Fuzz;

public class BotVsBotFuzzTests
{
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
        result.Violations.Should().BeEmpty(
            "no invariant should break. Repro: FuzzGameRunner.RunOnce(\""
            + $"{deckA}\", \"{deckB}\", {seed}, {MaxTurns}, 60s)\n"
            + string.Join("\n", result.Violations.Select(v => $"  [{v.Kind}] T{v.Turn}/{v.Phase}: {v.Detail}")));
    }
}
