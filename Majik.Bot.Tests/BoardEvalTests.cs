using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Tests.Helpers;
using Xunit;

namespace Majik.Bot.Tests;

public class BoardEvalTests
{
    [Fact]
    public void Score_IncreasesWith_SelfLife()
    {
        var s1 = new BotTestScenario(selfLife: 10);
        var s2 = new BotTestScenario(selfLife: 20);
        var w = ArchetypeWeights.Burn;
        BoardEval.Score(s2.Context, s2.Self, w).Should().BeGreaterThan(BoardEval.Score(s1.Context, s1.Self, w));
    }

    [Fact]
    public void Score_DecreasesWith_OpponentLife()
    {
        var s1 = new BotTestScenario(oppLife: 10);
        var s2 = new BotTestScenario(oppLife: 20);
        var w = ArchetypeWeights.Burn;
        BoardEval.Score(s1.Context, s1.Self, w).Should().BeGreaterThan(BoardEval.Score(s2.Context, s2.Self, w));
    }

    [Fact]
    public void Score_IncreasesWith_BotBoardPower()
    {
        var s1 = new BotTestScenario();
        var s2 = new BotTestScenario();
        s2.AddCreatureToBattlefield(s2.Self, "Grizzly Bears", 2, 2);
        var w = ArchetypeWeights.Prowess;
        BoardEval.Score(s2.Context, s2.Self, w).Should().BeGreaterThan(BoardEval.Score(s1.Context, s1.Self, w));
    }

    [Fact]
    public void Score_DecreasesWith_OpponentBoardPower()
    {
        var s1 = new BotTestScenario();
        var s2 = new BotTestScenario();
        s2.AddCreatureToBattlefield(s2.Opponent, "Tarmogoyf", 4, 5);
        var w = ArchetypeWeights.Burn;
        BoardEval.Score(s1.Context, s1.Self, w).Should().BeGreaterThan(BoardEval.Score(s2.Context, s2.Self, w));
    }

    [Fact]
    public void Score_IncreasesWith_ManaSources()
    {
        var s1 = new BotTestScenario();
        var s2 = new BotTestScenario();
        s2.AddLandToBattlefield(s2.Self, "Mountain");
        s2.AddLandToBattlefield(s2.Self, "Mountain");
        var w = ArchetypeWeights.BorosEnergy;
        BoardEval.Score(s2.Context, s2.Self, w).Should().BeGreaterThan(BoardEval.Score(s1.Context, s1.Self, w));
    }

    // ── Lethal-proximity term tests ─────────────────────────────────────────

    /// <summary>
    /// Opp at 3 life must score higher than opp at 15 life (all else equal).
    /// The lethal-proximity term should make the eval point the bot toward
    /// positions where the opponent is closer to losing.
    /// </summary>
    [Fact]
    public void Score_IsHigher_WhenOpponentCloserToLethal()
    {
        var near   = new BotTestScenario(oppLife: 3);   // opp nearly dead
        var safe   = new BotTestScenario(oppLife: 15);  // opp safe
        var w = ArchetypeWeights.Prowess;

        BoardEval.Score(near.Context, near.Self, w)
            .Should().BeGreaterThan(
                BoardEval.Score(safe.Context, safe.Self, w),
                because: "the eval should reward driving the opponent toward lethal");
    }

    /// <summary>
    /// Non-linearity test: the marginal gain of going 15→13 life (2 points
    /// in the safe zone) must be less than the marginal gain of going 3→1
    /// life (2 points in the danger zone). This validates the quadratic ramp.
    /// </summary>
    [Fact]
    public void Score_NonLinear_DangerZoneDamageMoreValuable()
    {
        var w = ArchetypeWeights.Prowess;

        // Two-point damage in the safe zone: opp 15→13
        var safe15 = new BotTestScenario(oppLife: 15);
        var safe13 = new BotTestScenario(oppLife: 13);
        double safeDelta = BoardEval.Score(safe13.Context, safe13.Self, w)
                         - BoardEval.Score(safe15.Context, safe15.Self, w);

        // Two-point damage in the danger zone: opp 3→1
        var low3 = new BotTestScenario(oppLife: 3);
        var low1 = new BotTestScenario(oppLife: 1);
        double dangerDelta = BoardEval.Score(low1.Context, low1.Self, w)
                           - BoardEval.Score(low3.Context, low3.Self, w);

        dangerDelta.Should().BeGreaterThan(safeDelta,
            because: "each point of damage near lethal (3→1) must be worth more " +
                     "than the same 2 points in the safe zone (15→13) — quadratic ramp");
    }

    /// <summary>
    /// LethalProximityBonus is zero at starting life (20) and grows as
    /// opp life decreases — basic monotonicity check on the helper itself.
    /// </summary>
    [Theory]
    [InlineData(20, 15)]
    [InlineData(15, 10)]
    [InlineData(10, 5)]
    [InlineData(5, 3)]
    [InlineData(3, 1)]
    public void LethalProximityBonus_IsStrictlyMonotone(int higherLife, int lowerLife)
    {
        BoardEval.LethalProximityBonus(lowerLife)
            .Should().BeGreaterThan(
                BoardEval.LethalProximityBonus(higherLife),
                because: $"proximity bonus at {lowerLife} must exceed bonus at {higherLife}");
    }

    /// <summary>
    /// Validate the concrete bonus values documented in the BoardEval XML comment.
    /// This guards the formula against accidental regressions in the constants.
    /// </summary>
    [Theory]
    [InlineData(20, 0)]   // baseline: no bonus at starting life
    [InlineData(15, 5)]   // linear only: 20-15 = 5
    [InlineData(10, 10)]  // linear only: 20-10 = 10
    [InlineData(5, 15)]   // threshold: 20-5 = 15 + (5-5)^2 = 0 → 15
    [InlineData(3, 21)]   // ramp: 20-3=17 + (5-3)^2=4 → 21
    [InlineData(1, 35)]   // ramp: 20-1=19 + (5-1)^2=16 → 35
    public void LethalProximityBonus_MatchesDocumentedValues(int oppLife, double expectedBonus)
    {
        BoardEval.LethalProximityBonus(oppLife)
            .Should().BeApproximately(expectedBonus, precision: 0.001,
                because: $"LethalProximityBonus({oppLife}) should be {expectedBonus} per the formula docs");
    }
}
