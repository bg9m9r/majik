using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// CR 702.44b / mana-provenance multiplicity — tests for the per-color
/// spent-count ledger (<see cref="Card.PendingCastColorCounts"/>) and the
/// "≥N of color X was spent" intervening-if predicate
/// (<see cref="Card.SpentAtLeast"/>).
///
/// The distinct-set <see cref="Card.PendingCastColors"/> can't tell
/// {R}{R} from {R}{G}; the count ledger preserves multiplicity so hybrid
/// Elemental Incarnations ("if {R}{R} was spent ...") can branch.
/// </summary>
public class PendingCastColorCountsTests
{
    private static Card MakeCard() => new Creature("Test", "{3}{R/G}{R/G}", 4, 4);

    // ---------------------------------------------------------------------
    // Counts ledger basics
    // ---------------------------------------------------------------------

    [Fact]
    public void RR_Spent_RecordsRedTwo()
    {
        var c = MakeCard();
        c.SetPendingCastColorCounts(new Dictionary<ManaColor, int>
        {
            [ManaColor.Red] = 2,
        });

        c.PendingCastColorCounts.Should().NotBeNull();
        c.PendingCastColorCounts![ManaColor.Red].Should().Be(2);
        c.PendingCastColorCounts.ContainsKey(ManaColor.Green).Should().BeFalse();
    }

    [Fact]
    public void RG_Spent_RecordsRedOneGreenOne()
    {
        var c = MakeCard();
        c.SetPendingCastColorCounts(new Dictionary<ManaColor, int>
        {
            [ManaColor.Red] = 1,
            [ManaColor.Green] = 1,
        });

        c.PendingCastColorCounts![ManaColor.Red].Should().Be(1);
        c.PendingCastColorCounts[ManaColor.Green].Should().Be(1);
    }

    [Fact]
    public void GG_Spent_RecordsGreenTwo()
    {
        var c = MakeCard();
        c.SetPendingCastColorCounts(new Dictionary<ManaColor, int>
        {
            [ManaColor.Green] = 2,
        });

        c.PendingCastColorCounts![ManaColor.Green].Should().Be(2);
        c.PendingCastColorCounts.ContainsKey(ManaColor.Red).Should().BeFalse();
    }

    // ---------------------------------------------------------------------
    // Distinct set derives from the counts (no regression for Sunburst)
    // ---------------------------------------------------------------------

    [Fact]
    public void SetCounts_DerivesDistinctColorsInWubrgOrder()
    {
        var c = MakeCard();
        c.SetPendingCastColorCounts(new Dictionary<ManaColor, int>
        {
            [ManaColor.Green] = 1,
            [ManaColor.White] = 2,
            [ManaColor.Red] = 1,
        });

        // Distinct colors derived from the ledger, canonical WUBRG order.
        c.PendingCastColors.Should().Equal(
            ManaColor.White, ManaColor.Red, ManaColor.Green);
    }

    [Fact]
    public void SetCounts_EmptyLedger_DistinctIsEmptyNotNull()
    {
        var c = MakeCard();
        c.SetPendingCastColorCounts(new Dictionary<ManaColor, int>());

        c.PendingCastColors.Should().NotBeNull().And.BeEmpty(
            "cast but no colored mana spent — empty, not null");
        c.PendingCastColorCounts.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Clear_ResetsBothLedgers()
    {
        var c = MakeCard();
        c.SetPendingCastColorCounts(new Dictionary<ManaColor, int>
        {
            [ManaColor.Red] = 2,
        });

        c.ClearPendingCastColors();

        c.PendingCastColors.Should().BeNull();
        c.PendingCastColorCounts.Should().BeNull();
    }

    [Fact]
    public void NoCast_BothLedgersNull()
    {
        var c = MakeCard();
        c.PendingCastColors.Should().BeNull();
        c.PendingCastColorCounts.Should().BeNull();
    }

    // ---------------------------------------------------------------------
    // Legacy SetPendingCastColors still works + back-fills counts
    // ---------------------------------------------------------------------

    [Fact]
    public void SetPendingCastColors_BackfillsCountsAsOnePerColor()
    {
        // Sunburst-style callers that only know distinct colors still work;
        // each distinct color back-fills a count of 1 so the predicate can
        // read a consistent ledger (a distinct color implies ≥1 spent).
        var c = MakeCard();
        c.SetPendingCastColors(new[] { ManaColor.White, ManaColor.Blue });

        c.PendingCastColors.Should().Equal(ManaColor.White, ManaColor.Blue);
        c.PendingCastColorCounts![ManaColor.White].Should().Be(1);
        c.PendingCastColorCounts[ManaColor.Blue].Should().Be(1);
    }

    // ---------------------------------------------------------------------
    // SpentAtLeast predicate — the "if {C}{C} was spent" gate
    // ---------------------------------------------------------------------

    [Fact]
    public void SpentAtLeast_RR_TrueWhenTwoRedSpent()
    {
        var c = MakeCard();
        c.SetPendingCastColorCounts(new Dictionary<ManaColor, int>
        {
            [ManaColor.Red] = 2,
        });

        c.SpentAtLeast(ManaColor.Red, 2).Should().BeTrue();
        c.SpentAtLeast(ManaColor.Green, 2).Should().BeFalse();
        c.SpentAtLeast(ManaColor.Red, 1).Should().BeTrue();
    }

    [Fact]
    public void SpentAtLeast_RG_NeitherColorReachesTwo()
    {
        var c = MakeCard();
        c.SetPendingCastColorCounts(new Dictionary<ManaColor, int>
        {
            [ManaColor.Red] = 1,
            [ManaColor.Green] = 1,
        });

        c.SpentAtLeast(ManaColor.Red, 2).Should().BeFalse();
        c.SpentAtLeast(ManaColor.Green, 2).Should().BeFalse();
        c.SpentAtLeast(ManaColor.Red, 1).Should().BeTrue();
        c.SpentAtLeast(ManaColor.Green, 1).Should().BeTrue();
    }

    [Fact]
    public void SpentAtLeast_GG_TrueForGreenOnly()
    {
        var c = MakeCard();
        c.SetPendingCastColorCounts(new Dictionary<ManaColor, int>
        {
            [ManaColor.Green] = 2,
        });

        c.SpentAtLeast(ManaColor.Green, 2).Should().BeTrue();
        c.SpentAtLeast(ManaColor.Red, 2).Should().BeFalse();
    }

    [Fact]
    public void SpentAtLeast_NoCast_AlwaysFalse()
    {
        var c = MakeCard();
        c.SpentAtLeast(ManaColor.Red, 1).Should().BeFalse(
            "no cast happened — nothing was spent");
    }

    [Fact]
    public void SpentAtLeast_NonPositiveCount_ThrowsOrTrue()
    {
        var c = MakeCard();
        c.SetPendingCastColorCounts(new Dictionary<ManaColor, int>
        {
            [ManaColor.Red] = 2,
        });

        // Asking for "at least 0" is trivially satisfiable but degenerate;
        // we guard against it to surface caller mistakes.
        Action act = () => c.SpentAtLeast(ManaColor.Red, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
