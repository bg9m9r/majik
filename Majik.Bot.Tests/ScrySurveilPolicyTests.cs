using FluentAssertions;
using Majik.Bot.Heuristic;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Xunit;

namespace Majik.Bot.Tests;

public class ScrySurveilPolicyTests
{
    [Fact]
    public void Scry_KeepsLands_WhenAlreadyManaScrewed()
    {
        var s = new BotTestScenario();
        var peeked = new ICard[]
        {
            new Land("Mountain"),
            new Creature("Goblin", "", 1, 1),
        };
        var decision = ScrySurveilPolicy.Scry(s.Context, s.Self, peeked);
        decision.TopOrder.Should().Contain(c => c.Name == "Mountain");
    }

    [Fact]
    public void Scry_BottomsLands_WhenManaFlooded()
    {
        var s = new BotTestScenario();
        for (var i = 0; i < 6; i++) s.AddLandToBattlefield(s.Self, $"Mountain{i}");
        var peeked = new ICard[]
        {
            new Land("Mountain"),
            new Creature("Goblin", "", 1, 1),
        };
        var decision = ScrySurveilPolicy.Scry(s.Context, s.Self, peeked);
        decision.ToBottom.Should().Contain(c => c.Name == "Mountain");
    }
}
