using FluentAssertions;
using Majik.Bot.Heuristic;
using Majik.Core.Cards;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Bot.Tests;

public class MulliganPolicyTests
{
    private static IReadOnlyList<ICard> Hand(int lands, int nonlands)
    {
        var hand = new List<ICard>();
        for (var i = 0; i < lands; i++) hand.Add(new Land($"Land{i}"));
        for (var i = 0; i < nonlands; i++) hand.Add(new Creature($"Creature{i}", "", 2, 2));
        return hand;
    }

    [Theory]
    [InlineData(0, 7, MulliganDecision.Mulligan)]
    [InlineData(1, 6, MulliganDecision.Mulligan)]
    [InlineData(2, 5, MulliganDecision.Keep)]
    [InlineData(3, 4, MulliganDecision.Keep)]
    [InlineData(4, 3, MulliganDecision.Keep)]
    [InlineData(5, 2, MulliganDecision.Keep)]
    [InlineData(6, 1, MulliganDecision.Mulligan)]
    [InlineData(7, 0, MulliganDecision.Mulligan)]
    public void Decide_BasedOnLandCount(int lands, int nonlands, MulliganDecision expected)
    {
        MulliganPolicy.Decide(Hand(lands, nonlands), mulligansTaken: 0).Should().Be(expected);
    }

    [Fact]
    public void AfterTwoMulligans_KeepsAggressively()
    {
        MulliganPolicy.Decide(Hand(1, 6), mulligansTaken: 3).Should().Be(MulliganDecision.Keep);
    }
}
