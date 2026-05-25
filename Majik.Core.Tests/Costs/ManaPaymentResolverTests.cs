using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

public class ManaPaymentResolverTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Pay_TapsRequestedSources_AddsManaToPool_DeductsCost()
    {
        var mountain1 = NamedCardFactory.Create("Mountain", _alice);
        var mountain2 = NamedCardFactory.Create("Mountain", _alice);
        mountain1.SetZone(ZoneType.Battlefield);
        mountain2.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice,
            ManaCost.Parse("1R"),
            new ManaPayment(new ICard[] { mountain1, mountain2 }));

        success.Should().BeTrue();
        ((Permanent)mountain1).IsTapped.Should().BeTrue();
        ((Permanent)mountain2).IsTapped.Should().BeTrue();
        _alice.ManaPool.Total.Should().Be(0); // both tapped → 2 mana, all spent
    }

    [Fact]
    public void Pay_InsufficientMana_ReturnsFalse_SourcesNotTapped()
    {
        var mountain = NamedCardFactory.Create("Mountain", _alice);
        mountain.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice,
            ManaCost.Parse("1R"),
            new ManaPayment(new[] { mountain }));

        success.Should().BeFalse();
        ((Permanent)mountain).IsTapped.Should().BeFalse();
    }

    [Fact]
    public void Pay_NonManaSource_Throws()
    {
        var bear = NamedCardFactory.Create("Grizzly Bears", _alice);
        var resolver = new ManaPaymentResolver();

        var act = () => resolver.Pay(_alice, ManaCost.Parse("R"),
            new ManaPayment(new[] { bear }));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mana ability*");
    }

    // -----------------------------------------------------------------------
    // CR 702.44b — Sunburst colors-spent ledger
    // -----------------------------------------------------------------------

    [Fact]
    public void Pay_ReportsColorsSpent_ForColoredPipCost()
    {
        // Pay {R} with one Mountain — Red is spent.
        var mountain = NamedCardFactory.Create("Mountain", _alice);
        mountain.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice,
            ManaCost.Parse("R"),
            new ManaPayment(new[] { mountain }),
            out var colors);

        success.Should().BeTrue();
        colors.Should().BeEquivalentTo(new[] { ManaColor.Red });
    }

    [Fact]
    public void Pay_ReportsColorsSpent_ForGenericPaidWithColoredMana()
    {
        // Pay {2} with two Mountains — both Red even though the cost is
        // entirely generic. CR 702.44b: generic mana paid with colored
        // mana counts toward Sunburst.
        var m1 = NamedCardFactory.Create("Mountain", _alice);
        var m2 = NamedCardFactory.Create("Mountain", _alice);
        m1.SetZone(ZoneType.Battlefield);
        m2.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice,
            ManaCost.Parse("2"),
            new ManaPayment(new ICard[] { m1, m2 }),
            out var colors);

        success.Should().BeTrue();
        colors.Should().Contain(ManaColor.Red,
            "generic mana paid with Red mana counts as Red spent (CR 702.44b)");
        colors.Should().HaveCount(1);
    }

    [Fact]
    public void Pay_ReportsMultipleDistinctColorsSpent()
    {
        // Pay {3} with WUBR — Etched-Oracle-shape Sunburst sample.
        var plains = NamedCardFactory.Create("Plains", _alice);
        var island = NamedCardFactory.Create("Island", _alice);
        var swamp = NamedCardFactory.Create("Swamp", _alice);
        var mountain = NamedCardFactory.Create("Mountain", _alice);
        foreach (var l in new[] { plains, island, swamp, mountain })
        {
            l.SetZone(ZoneType.Battlefield);
        }
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice,
            ManaCost.Parse("4"),
            new ManaPayment(new ICard[] { plains, island, swamp, mountain }),
            out var colors);

        success.Should().BeTrue();
        colors.Should().BeEquivalentTo(new[]
        {
            ManaColor.White, ManaColor.Blue, ManaColor.Black, ManaColor.Red,
        });
    }

    [Fact]
    public void Pay_ReportsEmptyColorsSpent_WhenPaidFromGenericFloating()
    {
        // Float 2 generic mana, then pay {2} — no colored mana consumed.
        _alice.AddManaToPool(ManaCost.Parse("2"));
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice,
            ManaCost.Parse("2"),
            ManaPayment.Empty,
            out var colors);

        success.Should().BeTrue();
        colors.Should().BeEmpty(
            "no colored mana was spent → empty colors-spent ledger");
    }
}
