using FluentAssertions;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.ValueObjects;

/// <summary>
/// Pay-down of the <c>colorless-pip-cost-and-mana</c> deferral: a true {C}
/// colorless-mana pip (CR 107.4c) distinct from a generic {N} pip.
///
/// CR 107.4c — "{C}" in a cost means one colorless mana. CR 106.1b — colorless
/// is a real mana <em>type</em> (not a color), produced by sources like Eye of
/// Ugin / Wastes / Karn's Bastion. A {C} pip can be paid ONLY by colorless mana
/// (CR 601.2g / 118.5) — generic mana, colored mana, or "any color" mana does
/// NOT satisfy it. Conversely colorless mana is perfectly good for paying a
/// generic {N} requirement (CR 106.1c).
/// </summary>
public class ColorlessPipManaTests
{
    // --- Parsing: {C} is its own pip column, separate from generic {N} -------

    [Fact]
    public void Parse_ColorlessPip_TaggedColorlessWithinGeneric()
    {
        var cost = ManaCost.Parse("{1}{C}");

        // Colorless is a tagged subset of Generic: {1}{C} => 2 generic units,
        // one of which is colorless-typed (CR 107.4c / 106.1b).
        cost.Generic.Should().Be(2, "{1}{C} = two generic-value units");
        cost.Colorless.Should().Be(1, "{C} marks one of them colorless (CR 107.4c)");
        cost.TotalValue.Should().Be(2, "colorless is not added on top of generic");
    }

    [Fact]
    public void Parse_MultipleColorlessPips_Accumulate()
    {
        var cost = ManaCost.Parse("{C}{C}");

        cost.Colorless.Should().Be(2);
        cost.Generic.Should().Be(2, "both {C} pips count toward generic value");
        cost.TotalValue.Should().Be(2);
    }

    [Fact]
    public void ToString_RoundTripsColorlessPip()
    {
        ManaCost.Parse("{1}{C}").ToString().Should().Be("1C");
    }

    [Fact]
    public void Equality_DistinguishesColorlessFromGeneric()
    {
        // {1}{C} (one generic + one colorless) is NOT the same cost as {2}
        // (two generic) — they demand different mana.
        ManaCost.Parse("{1}{C}").Should().NotBe(ManaCost.Parse("2"));
        ManaCost.Parse("{C}").Should().Be(ManaCost.Parse("{C}"));
    }

    // --- Pool: colorless mana lives in its own bucket ------------------------

    [Fact]
    public void Pool_Add_TracksColorlessSubsetOfGeneric()
    {
        var pool = ManaPool.Empty.Add(ManaCost.Parse("{1}{C}"));

        pool.Colorless.Should().Be(1, "the {C} mana is colorless-typed");
        pool.Generic.Should().Be(2, "colorless mana also counts toward generic (CR 106.1c)");
        pool.Total.Should().Be(2);
    }

    // --- Payment: the {C} pip demands colorless mana (CR 107.4c) -------------

    [Fact]
    public void GenericManaCannotPayColorlessPip()
    {
        // Two generic mana floating; cost is {1}{C}. The {1} is covered, but
        // the {C} pip cannot be paid from generic mana.
        var pool = ManaPool.Empty.AddGeneric(2);

        pool.CanPay(ManaCost.Parse("{1}{C}")).Should().BeFalse(
            "generic mana cannot satisfy a {C} colorless pip (CR 107.4c)");
    }

    [Fact]
    public void ColoredManaCannotPayColorlessPip()
    {
        var pool = ManaPool.Empty.AddColored(red: 1, blue: 1);

        pool.CanPay(ManaCost.Parse("{C}")).Should().BeFalse(
            "colored mana cannot satisfy a {C} colorless pip (CR 107.4c)");
    }

    [Fact]
    public void ColorlessManaPaysColorlessPip()
    {
        var pool = ManaPool.Empty.Add(ManaCost.Parse("{1}{C}"));

        pool.CanPay(ManaCost.Parse("{1}{C}")).Should().BeTrue();
        var (after, ok) = pool.Pay(ManaCost.Parse("{1}{C}"));
        ok.Should().BeTrue();
        after.Total.Should().Be(0);
    }

    [Fact]
    public void ColorlessManaCanPayGenericRequirement()
    {
        // CR 106.1c — colorless mana is fine for generic costs.
        var pool = ManaPool.Empty.Add(ManaCost.Parse("{C}{C}"));

        pool.CanPay(ManaCost.Parse("2")).Should().BeTrue(
            "colorless mana satisfies a generic {N} pip");
        var (after, ok) = pool.Pay(ManaCost.Parse("2"));
        ok.Should().BeTrue();
        after.Total.Should().Be(0);
    }

    [Fact]
    public void ColorlessPaysItsOwnPipFirst_LeftoverPaysGeneric()
    {
        // {C}{C} floating; cost {1}{C}. One colorless pays the {C} pip; the
        // remaining colorless covers the {1} generic. Nothing left over.
        var pool = ManaPool.Empty.Add(ManaCost.Parse("{C}{C}"));

        var (after, ok) = pool.Pay(ManaCost.Parse("{1}{C}"));
        ok.Should().BeTrue();
        after.Total.Should().Be(0);
    }

    [Fact]
    public void ColorlessPipNotPayable_WhenOnlyOneColorlessAndItIsNeededForGeneric()
    {
        // One colorless + one red. Cost {1}{C}: the {C} pip needs the lone
        // colorless, leaving the {1} to the red — payable.
        var pool = ManaPool.Empty.AddColored(red: 1).Add(ManaCost.Parse("{C}"));

        pool.CanPay(ManaCost.Parse("{1}{C}")).Should().BeTrue();

        // But {C}{C} (two colorless pips) is NOT payable with only one colorless.
        pool.CanPay(ManaCost.Parse("{C}{C}")).Should().BeFalse(
            "two {C} pips need two colorless mana; red can't substitute");
    }
}
