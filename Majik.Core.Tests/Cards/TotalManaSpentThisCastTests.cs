using System;
using FluentAssertions;
using Majik.Core.Cards;
using Xunit;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// CR 118.10 — tests for the total-amount spent-mana sentinel
/// (<see cref="Card.TotalManaSpentThisCast"/>) and the "≥N total mana was
/// spent" intervening-if predicate (<see cref="Card.SpentAtLeastTotal"/>).
///
/// This is the magnitude sibling of the per-color count ledger
/// (<see cref="Card.PendingCastColorCounts"/> / <see cref="Card.SpentAtLeast"/>):
/// the count ledger answers "how much of color X"; this answers "how much in
/// total" — the gate the "if {N} or more mana was spent to cast it" payoffs
/// read.
/// </summary>
public class TotalManaSpentThisCastTests
{
    private static Card MakeCard() => new Creature("Test", "{3}{R}", 4, 4);

    [Fact]
    public void NoCast_TotalIsZero()
    {
        MakeCard().TotalManaSpentThisCast.Should().Be(0);
    }

    [Fact]
    public void SetTotal_RecordsValue()
    {
        var c = MakeCard();
        c.SetTotalManaSpentThisCast(4);
        c.TotalManaSpentThisCast.Should().Be(4);
    }

    [Fact]
    public void SetTotal_NegativeClampsToZero()
    {
        var c = MakeCard();
        c.SetTotalManaSpentThisCast(-3);
        c.TotalManaSpentThisCast.Should().Be(0);
    }

    [Fact]
    public void Clear_ResetsToZero()
    {
        var c = MakeCard();
        c.SetTotalManaSpentThisCast(6);
        c.ClearTotalManaSpentThisCast();
        c.TotalManaSpentThisCast.Should().Be(0);
    }

    [Fact]
    public void SpentAtLeastTotal_TrueWhenReached()
    {
        var c = MakeCard();
        c.SetTotalManaSpentThisCast(4);

        c.SpentAtLeastTotal(4).Should().BeTrue();
        c.SpentAtLeastTotal(3).Should().BeTrue();
        c.SpentAtLeastTotal(1).Should().BeTrue();
    }

    [Fact]
    public void SpentAtLeastTotal_FalseWhenBelow()
    {
        var c = MakeCard();
        c.SetTotalManaSpentThisCast(3);

        c.SpentAtLeastTotal(4).Should().BeFalse();
        c.SpentAtLeastTotal(5).Should().BeFalse();
    }

    [Fact]
    public void SpentAtLeastTotal_FreeCast_AlwaysFalse()
    {
        var c = MakeCard();
        // No mana spent (free cast / no cast) — total is 0.
        c.SpentAtLeastTotal(1).Should().BeFalse();
    }

    [Fact]
    public void SpentAtLeastTotal_NonPositiveAmount_Throws()
    {
        var c = MakeCard();
        c.SetTotalManaSpentThisCast(4);

        Action act = () => c.SpentAtLeastTotal(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
