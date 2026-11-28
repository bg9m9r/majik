using FluentAssertions;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.ValueObjects;

/// <summary>CR 107.4 — mana costs in mana-symbol (brace) form, and what is
/// left unpaid after the floating pool (CR 601.2g-h, CR 106.1c).</summary>
public class ManaCostBraceTests
{
    [Theory]
    [InlineData("{R}", "{R}")]
    [InlineData("{2}{R}", "{2}{R}")]
    [InlineData("1WU", "{1}{W}{U}")]
    [InlineData("{X}{R}{R}", "{X}{R}{R}")]
    [InlineData("{C}{C}", "{C}{C}")]
    [InlineData("{3}{C}", "{3}{C}")]
    [InlineData("{R/G}{R/G}", "{R/G}{R/G}")]
    [InlineData("{2/W}", "{2/W}")]
    [InlineData("{1}{U/P}", "{1}{U/P}")]
    [InlineData("{0}", "{0}")]
    public void ToBraceString(string cost, string expected) =>
        ManaCost.Parse(cost).ToBraceString().Should().Be(expected);

    [Fact]
    public void RemainingAfter_TheColouredPipsTakeTheirColour_AndTheRestPaysGeneric()
    {
        var cost = ManaCost.Parse("{2}{R}{G}");
        cost.RemainingAfter(ManaPool.Empty).Should().Be("{2}{R}{G}");
        cost.RemainingAfter(ManaPool.Empty.AddColored(red: 1)).Should().Be("{2}{G}");
        cost.RemainingAfter(ManaPool.Empty.AddColored(red: 2)).Should().Be("{1}{G}", "the second R pays generic");
        cost.RemainingAfter(ManaPool.Empty.AddColored(red: 1, green: 1, generic: 2)).Should().Be("{0}");
    }

    [Fact]
    public void RemainingAfter_AColorlessPip_NeedsColorlessMana()
    {
        var cost = ManaCost.Parse("{1}{C}");
        cost.RemainingAfter(ManaPool.Empty.AddColored(red: 2)).Should().Be("{C}");
        cost.RemainingAfter(ManaPool.Empty.AddColored(colorless: 1)).Should().Be("{1}");
    }
}