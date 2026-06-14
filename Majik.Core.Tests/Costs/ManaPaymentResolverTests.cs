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

    // -----------------------------------------------------------------------
    // CR 609.4b — "spend mana as though it were mana of any color" permission
    // (Robber of the Rich / Fist of Suns). A tagged cast may pay colored pips
    // with mana of ANY color: the color requirement is relaxed, total cost
    // unchanged.
    // -----------------------------------------------------------------------

    [Fact]
    public void Pay_SpendAsAnyColor_PaysColoredPip_WithWrongColorMana()
    {
        // {R} colored pip, but the only source is a Forest (green). Without the
        // permission this fails (green can't pay a red pip); WITH it, green
        // satisfies the red requirement.
        var forest = NamedCardFactory.Create("Forest", _alice);
        forest.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice,
            ManaCost.Parse("R"),
            new ManaPayment(new[] { forest }),
            spendAsAnyColor: true);

        success.Should().BeTrue();
        ((Permanent)forest).IsTapped.Should().BeTrue();
        _alice.ManaPool.Total.Should().Be(0);
    }

    [Fact]
    public void Pay_WithoutPermission_CannotPayColoredPip_WithWrongColorMana()
    {
        // Control: same setup, NO permission → green can't pay {R}.
        var forest = NamedCardFactory.Create("Forest", _alice);
        forest.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice,
            ManaCost.Parse("R"),
            new ManaPayment(new[] { forest }));

        success.Should().BeFalse();
        ((Permanent)forest).IsTapped.Should().BeFalse();
    }

    [Fact]
    public void Pay_SpendAsAnyColor_StillRequiresEnoughTotalMana()
    {
        // {R}{R} needs two mana — one Forest can't cover it even with the
        // any-color permission (permission relaxes color, not amount; CR 106.6).
        var forest = NamedCardFactory.Create("Forest", _alice);
        forest.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice,
            ManaCost.Parse("RR"),
            new ManaPayment(new[] { forest }),
            spendAsAnyColor: true);

        success.Should().BeFalse();
        ((Permanent)forest).IsTapped.Should().BeFalse();
    }

    [Fact]
    public void Pay_SpendAsAnyColor_PaysMultiColorCost_FromSingleColorSources()
    {
        // {W}{U}{B} paid entirely from three Forests — five-color fixing via
        // the permission.
        var f1 = NamedCardFactory.Create("Forest", _alice);
        var f2 = NamedCardFactory.Create("Forest", _alice);
        var f3 = NamedCardFactory.Create("Forest", _alice);
        f1.SetZone(ZoneType.Battlefield);
        f2.SetZone(ZoneType.Battlefield);
        f3.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice,
            ManaCost.Parse("WUB"),
            new ManaPayment(new ICard[] { f1, f2, f3 }),
            spendAsAnyColor: true);

        success.Should().BeTrue();
        _alice.ManaPool.Total.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // TryAutoSelectSources — portal "Auto-pay" (empty source list) support
    // -----------------------------------------------------------------------

    [Fact]
    public void TryAutoSelectSources_PicksUntappedSourcesToCoverCost()
    {
        // Two untapped Forests cover {1}{G}.
        var f1 = NamedCardFactory.Create("Forest", _alice);
        var f2 = NamedCardFactory.Create("Forest", _alice);
        OnBattlefield(f1, f2);
        var resolver = new ManaPaymentResolver();

        var ok = resolver.TryAutoSelectSources(_alice, ManaCost.Parse("1G"), out var payment);

        ok.Should().BeTrue();
        payment.Sources.Should().HaveCount(2);
        payment.IsCancelled.Should().BeFalse();

        resolver.Pay(_alice, ManaCost.Parse("1G"), payment).Should().BeTrue();
        ((Permanent)f1).IsTapped.Should().BeTrue();
        ((Permanent)f2).IsTapped.Should().BeTrue();
    }

    [Fact]
    public void TryAutoSelectSources_PrefersColorMatchingSource()
    {
        // {R}: a Forest and a Mountain are untapped; only the Mountain is
        // selected (it produces the needed R).
        var forest = NamedCardFactory.Create("Forest", _alice);
        var mountain = NamedCardFactory.Create("Mountain", _alice);
        OnBattlefield(forest, mountain);
        var resolver = new ManaPaymentResolver();

        var ok = resolver.TryAutoSelectSources(_alice, ManaCost.Parse("R"), out var payment);

        ok.Should().BeTrue();
        payment.Sources.Should().ContainSingle().Which.Should().BeSameAs(mountain);
    }

    [Fact]
    public void TryAutoSelectSources_UsesFloatingPoolBeforeSelectingSources()
    {
        // 1 floating G already covers the colored pip of {1}{G}; one Forest
        // covers the generic remainder.
        _alice.AddManaToPool(ManaCost.Parse("G"));
        var forest = NamedCardFactory.Create("Forest", _alice);
        OnBattlefield(forest);
        var resolver = new ManaPaymentResolver();

        var ok = resolver.TryAutoSelectSources(_alice, ManaCost.Parse("1G"), out var payment);

        ok.Should().BeTrue();
        payment.Sources.Should().ContainSingle().Which.Should().BeSameAs(forest);
    }

    [Fact]
    public void TryAutoSelectSources_InsufficientSources_ReturnsFalse_EmptyPayment()
    {
        // {G}{G} but only one untapped Forest.
        var forest = NamedCardFactory.Create("Forest", _alice);
        OnBattlefield(forest);
        var resolver = new ManaPaymentResolver();

        var ok = resolver.TryAutoSelectSources(_alice, ManaCost.Parse("GG"), out var payment);

        ok.Should().BeFalse();
        payment.Should().BeSameAs(ManaPayment.Empty);
        ((Permanent)forest).IsTapped.Should().BeFalse("selection-only — nothing tapped.");
    }

    [Fact]
    public void TryAutoSelectSources_SkipsTappedSources()
    {
        // {G}: the only Forest is already tapped → can't auto-select.
        var forest = NamedCardFactory.Create("Forest", _alice);
        OnBattlefield(forest);
        ((Permanent)forest).Tap();
        var resolver = new ManaPaymentResolver();

        var ok = resolver.TryAutoSelectSources(_alice, ManaCost.Parse("G"), out var payment);

        ok.Should().BeFalse();
        payment.Should().BeSameAs(ManaPayment.Empty);
    }

    [Fact]
    public void TryAutoSelectSources_HybridCost_ReturnsFalse_OutOfScope()
    {
        // {R/G} hybrid pip needs an explicit choice — auto-select bails.
        var forest = NamedCardFactory.Create("Forest", _alice);
        OnBattlefield(forest);
        var resolver = new ManaPaymentResolver();

        var ok = resolver.TryAutoSelectSources(_alice, ManaCost.Parse("{R/G}"), out var payment);

        ok.Should().BeFalse();
        payment.Should().BeSameAs(ManaPayment.Empty);
    }

    [Fact]
    public void TryAutoSelectSources_MultiManaSource_CoversTwoGeneric_WithOneTap()
    {
        // CR 605 — Sol Ring's "{T}: Add {C}{C}" produces TWO generic from a
        // single tap. Paying {2} must select ONLY the Sol Ring (one source);
        // the greedy generic loop previously selected one source per generic
        // UNIT, so it would have also tapped the Forest and floated the
        // surplus mana (deferral mana-payment-over-select, residual (c)).
        var solRing = NamedCardFactory.Create("Sol Ring", _alice);
        var forest = NamedCardFactory.Create("Forest", _alice);
        OnBattlefield(solRing, forest);
        var resolver = new ManaPaymentResolver();

        var ok = resolver.TryAutoSelectSources(_alice, ManaCost.Parse("2"), out var payment);

        ok.Should().BeTrue();
        payment.Sources.Should().ContainSingle()
            .Which.Should().BeSameAs(solRing);
    }

    [Fact]
    public void TryAutoSelectSources_MultiManaSource_CoversColoredPlusGeneric_NoExtraTap()
    {
        // Sol Ring ({C}{C}) covers both generic units of {2}{G}; a single
        // Forest covers the {G}. Two sources total, not three — the surplus-
        // source over-select must not fire after a 2-output source.
        var solRing = NamedCardFactory.Create("Sol Ring", _alice);
        var forest = NamedCardFactory.Create("Forest", _alice);
        var spare = NamedCardFactory.Create("Forest", _alice);
        OnBattlefield(solRing, forest, spare);
        var resolver = new ManaPaymentResolver();

        var ok = resolver.TryAutoSelectSources(_alice, ManaCost.Parse("2G"), out var payment);

        ok.Should().BeTrue();
        payment.Sources.Should().HaveCount(2);
        payment.Sources.Should().Contain(solRing);
    }

    [Fact]
    public void TryAutoSelectSources_MultiColoredSource_CoversTwoColoredPips_WithOneTap()
    {
        // CR 605 / 106.1c — a source whose mana ability adds {G}{G} (two of the
        // SAME color) covers BOTH colored pips of {G}{G} with a single tap.
        // The colored-selection loop previously picked one source per colored
        // UNIT (symmetric to the generic over-select residual (c)): the first
        // {G} grabbed the {G}{G} source, then the second {G} found no OTHER
        // unused green source → auto-select FAILED for a payable cost
        // (under-select). Per-source per-color accounting must subtract the
        // {G}{G} source's full green yield before looking for the next source.
        var dualGreen = MakeMultiColorSource("GG");
        OnBattlefield(dualGreen);
        var resolver = new ManaPaymentResolver();

        var ok = resolver.TryAutoSelectSources(_alice, ManaCost.Parse("GG"), out var payment);

        ok.Should().BeTrue("a single {G}{G} source covers both green pips (CR 605)");
        payment.Sources.Should().ContainSingle().Which.Should().BeSameAs(dualGreen);
    }

    [Fact]
    public void TryAutoSelectSources_MultiColoredSource_DoesNotOverSelectExtraColorSource()
    {
        // A {G}{G} source covers both green pips of {G}{G}; a spare Forest must
        // NOT also be tapped. Pre-fix the per-unit colored loop grabbed the
        // {G}{G} source for the first pip then the spare Forest for the second,
        // tapping two sources and floating the surplus green (cosmetic
        // tap-waste — the colored analogue of the generic residual (c)).
        var dualGreen = MakeMultiColorSource("GG");
        var spare = NamedCardFactory.Create("Forest", _alice);
        OnBattlefield(dualGreen, spare);
        var resolver = new ManaPaymentResolver();

        var ok = resolver.TryAutoSelectSources(_alice, ManaCost.Parse("GG"), out var payment);

        ok.Should().BeTrue();
        payment.Sources.Should().ContainSingle()
            .Which.Should().BeSameAs(dualGreen);
    }

    [Fact]
    public void TryAutoSelectSources_MultiColoredSource_CoversColoredThenGeneric()
    {
        // A {G}{G} source has surplus after one {G} pip; the leftover green
        // pays a generic unit too (CR 106.1c — generic accepts any mana). {1}{G}
        // is therefore covered by the single {G}{G} source — no second tap.
        var dualGreen = MakeMultiColorSource("GG");
        var spare = NamedCardFactory.Create("Forest", _alice);
        OnBattlefield(dualGreen, spare);
        var resolver = new ManaPaymentResolver();

        var ok = resolver.TryAutoSelectSources(_alice, ManaCost.Parse("1G"), out var payment);

        ok.Should().BeTrue();
        payment.Sources.Should().ContainSingle()
            .Which.Should().BeSameAs(dualGreen);
    }

    /// <summary>Build a battlefield permanent whose single mana ability adds the
    /// parsed multi-mana amount (e.g. "GG" → "{T}: Add {G}{G}"), for exercising
    /// the multi-color auto-select packing. Uses a plain Forest shell + an
    /// explicit <see cref="ManaAbility"/> so the produced amount is fixed.</summary>
    private ICard MakeMultiColorSource(string amount)
    {
        var card = NamedCardFactory.Create("Forest", _alice);
        var concrete = (Permanent)card;
        // Replace the printed single-G ability with the multi-mana one so
        // EffectiveManaAbilities.For sees exactly one ability producing `amount`.
        foreach (var existing in concrete.Abilities.OfType<ManaAbility>().ToList())
        {
            concrete.RemoveAbility(existing);
        }
        concrete.AddAbility(new ManaAbility(concrete, _alice, ManaCost.Parse(amount)));
        return card;
    }

    private void OnBattlefield(params ICard[] cards)
    {
        foreach (var c in cards)
        {
            c.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(c);
        }
    }
}
