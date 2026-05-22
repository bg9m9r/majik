using FluentAssertions;
using Majik.Bot.Heuristic;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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

    [Fact]
    public void NoEarlyCurve_MulligansEvenWithGoodLandCount()
    {
        // 3 lands + 4 nonlands, but every nonland costs 5+ mana — no curve
        // at turn 1-2. Should mulligan at zero mulligans taken.
        var hand = new List<ICard>
        {
            new Land("L1"), new Land("L2"), new Land("L3"),
            new Creature("Heavy1", "4G", 5, 5),
            new Creature("Heavy2", "4G", 5, 5),
            new Creature("Heavy3", "4G", 5, 5),
            new Creature("Heavy4", "4G", 5, 5),
        };
        MulliganPolicy.Decide(hand, mulligansTaken: 0).Should().Be(MulliganDecision.Mulligan);
    }

    [Fact]
    public void NoColorSupport_Mulligans()
    {
        // 3 Forests + 4 colored-red nonlands. No red sources → mulligan.
        var hand = new List<ICard>
        {
            new Land("F1", subtypes: new[] { CardSubtype.Forest }),
            new Land("F2", subtypes: new[] { CardSubtype.Forest }),
            new Land("F3", subtypes: new[] { CardSubtype.Forest }),
            new Creature("Bolt-creature", "R", 1, 1),
            new Creature("Bolt2", "R", 1, 1),
            new Creature("Bolt3", "1R", 2, 1),
            new Creature("Bolt4", "1R", 2, 1),
        };
        MulliganPolicy.Decide(hand, mulligansTaken: 0).Should().Be(MulliganDecision.Mulligan);
    }

    [Fact]
    public void ColorSupportPresent_Keeps()
    {
        // Same hand but with Mountains — red supported.
        var hand = new List<ICard>
        {
            new Land("M1", subtypes: new[] { CardSubtype.Mountain }),
            new Land("M2", subtypes: new[] { CardSubtype.Mountain }),
            new Land("M3", subtypes: new[] { CardSubtype.Mountain }),
            new Creature("Burn1", "R", 1, 1),
            new Creature("Burn2", "R", 1, 1),
            new Creature("Burn3", "1R", 2, 1),
            new Creature("Burn4", "1R", 2, 1),
        };
        MulliganPolicy.Decide(hand, mulligansTaken: 0).Should().Be(MulliganDecision.Keep);
    }
}
