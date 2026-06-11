using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Bot.OpponentModel;
using Xunit;

namespace Majik.Bot.Tests.OpponentModel;

public class ArchetypeInferencerTests
{
    private static readonly ArchetypeInferencer Inf = new();

    [Fact]
    public void NoPublicCards_PosteriorEqualsPrior()
    {
        var belief = Inf.Infer(System.Array.Empty<string>());
        foreach (var aw in belief)
            aw.Weight.Should().BeApproximately(MetagamePrior.Weight(aw.Archetype), 1e-9);
        belief.Sum(aw => aw.Weight).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void SignatureCards_PushTheirArchetypeToTheTop()
    {
        // Pick 2-3 LOW-FREQUENCY signature cards actually in Burn's list. PRINT
        // BotDeckCatalog.Get("Burn") first if unsure; choose names appearing in few archetypes.
        var burn = BotDeckCatalog.Get("Burn");
        var signature = burn.Where(n => n is "Goblin Guide" or "Monastery Swiftspear" or "Lava Spike").Take(3).ToList();
        signature.Should().NotBeEmpty("test needs real Burn signature cards");
        var belief = Inf.Infer(signature);
        belief.OrderByDescending(aw => aw.Weight).First().Archetype.Should().Be("Burn");
        belief.Sum(aw => aw.Weight).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void OutOfDistributionCard_DoesNotCrash_AndDoesNotShiftBelief()
    {
        var belief = Inf.Infer(new[] { "Totally Fake Card Name 9000" });
        foreach (var aw in belief)
            aw.Weight.Should().BeApproximately(MetagamePrior.Weight(aw.Archetype), 1e-9);
    }
}
