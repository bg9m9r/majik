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
}
