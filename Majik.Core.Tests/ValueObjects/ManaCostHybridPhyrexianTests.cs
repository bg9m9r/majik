using FluentAssertions;
using Majik.Core.ValueObjects;
using Xunit;

public class ManaCostHybridPhyrexianTests
{
    // ---------- Hybrid ----------

    [Fact]
    public void Parse_MonocoloredHybrid_RG()
    {
        var c = ManaCost.Parse("{R/G}");
        c.HybridPips.Should().HaveCount(1);
        c.HybridPips[0].Color1.Should().Be(ManaColor.Red);
        c.HybridPips[0].Color2.Should().Be(ManaColor.Green);
        c.TotalValue.Should().Be(1);
    }

    [Fact]
    public void Parse_TwoOrColorHybrid_2W()
    {
        var c = ManaCost.Parse("{2/W}");
        c.HybridPips.Should().HaveCount(1);
        c.HybridPips[0].Color1.Should().Be(ManaColor.Generic);
        c.HybridPips[0].Color2.Should().Be(ManaColor.White);
        c.HybridPips[0].GenericAlternative.Should().Be(2);
        c.TotalValue.Should().Be(2);
    }

    [Fact]
    public void Parse_MixedCost_WithHybridAndPlainColored()
    {
        var c = ManaCost.Parse("1W{R/G}");
        c.Generic.Should().Be(1);
        c.White.Should().Be(1);
        c.HybridPips.Should().HaveCount(1);
        c.TotalValue.Should().Be(3);
    }

    // ---------- Phyrexian ----------

    [Fact]
    public void Parse_Phyrexian_UP()
    {
        var c = ManaCost.Parse("{U/P}");
        c.PhyrexianPips.Should().HaveCount(1);
        c.PhyrexianPips[0].Should().Be(ManaColor.Blue);
        c.TotalValue.Should().Be(1);
    }

    [Fact]
    public void Parse_MultiplePhyrexian_Combined()
    {
        var c = ManaCost.Parse("{U/P}{U/P}{U/P}{U/P}");
        c.PhyrexianPips.Should().HaveCount(4);
        c.TotalValue.Should().Be(4);
    }

    [Fact]
    public void Parse_PhyrexianAndHybridAndPlain_MixedCost()
    {
        var c = ManaCost.Parse("2W{R/G}{B/P}");
        c.Generic.Should().Be(2);
        c.White.Should().Be(1);
        c.HybridPips.Should().HaveCount(1);
        c.PhyrexianPips.Should().HaveCount(1);
        c.PhyrexianPips[0].Should().Be(ManaColor.Black);
        c.TotalValue.Should().Be(5);
    }

    [Fact]
    public void Parse_BareColorString_StillWorks_BackwardCompatible()
    {
        var c = ManaCost.Parse("3RR");
        c.Generic.Should().Be(3);
        c.Red.Should().Be(2);
        c.HybridPips.Should().BeEmpty();
        c.PhyrexianPips.Should().BeEmpty();
    }
}
