using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Majik.Bot.Tests.Integration.Fuzz;

public class FuzzGameRunnerTests
{
    [Fact]
    public async Task RunOnce_BurnVsBoros_CompletesWithoutViolations()
    {
        var result = await FuzzGameRunner.RunOnce(
            deckA: "Burn", deckB: "BorosEnergy", seed: 1, maxTurns: 20,
            timeout: TimeSpan.FromSeconds(60));

        result.Violations.Should().BeEmpty(
            because: "a clean bot-vs-bot game must not breach engine invariants:\n"
                + string.Join("\n", result.Violations.Select(v => $"  [{v.Kind}] {v.Detail}")));
        result.TimedOut.Should().BeFalse();
    }
}
