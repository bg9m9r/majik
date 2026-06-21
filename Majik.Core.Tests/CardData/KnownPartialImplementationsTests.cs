using FluentAssertions;
using Majik.Core.CardData;
using Xunit;

namespace Majik.Core.Tests.CardData;

public class KnownPartialImplementationsTests
{
    [Fact]
    public void Registry_RecordsAgatha_AsPartial()
    {
        KnownPartialImplementations.TryGet("Agatha's Soul Cauldron", out var gap)
            .Should().BeTrue("Agatha's ability-grant static is a documented partial");
        gap.Severity.Should().Be(CardGapSeverity.Partial);
        gap.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryGet_UnknownCard_ReturnsFalse()
    {
        KnownPartialImplementations.TryGet("Definitely Not A Real Card", out _)
            .Should().BeFalse();
    }

    // Staleness guard: Tameshi, Reality Architect was a documented Partial
    // ("Blocked on a per-activation X ledger") until the per-activation X ledger
    // shipped (GAP 2: ActivatedAbility.ChosenX + ResolutionContext.ChosenX). Its
    // reanimation ability now emits via that ledger
    // (see TameshiRealityArchitectFactory + TameshiRealityArchitectTests), so the
    // card is FULL and must NOT carry a stale registry entry. This locks the
    // cleanup in so the Partial entry can never be silently re-introduced.
    [Fact]
    public void Registry_DoesNotRecordTameshi_NowFullViaChosenXLedger()
    {
        KnownPartialImplementations.TryGet("Tameshi, Reality Architect", out _)
            .Should().BeFalse(
                "Tameshi is FULL since the per-activation X ledger (ChosenX) "
                + "shipped; its reanimation ability is no longer deferred");
    }
}
