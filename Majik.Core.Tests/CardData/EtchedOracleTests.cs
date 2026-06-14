using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Tests.Helpers;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="EtchedOracleFactory"/> (Fifth Dawn, {4}).
///
/// Covers:
/// - Identity (Artifact Creature — Wizard, {4} 0/0, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Sunburst ETB lands +1/+1 counters (CR 702.44a — creature branch).
/// - {1}, Remove four +1/+1 counters: target player draws three cards
///   (the counter-removal is a DECLARED AdditionalCost.RemoveCounters cost).
/// </summary>
public class EtchedOracleTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void SeedLibrary(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Filler-{p.Name}-{i}", "{0}");
            c.SetOwner(p);
            p.Zones.Library.AddCard(c);
        }
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EtchedOracle_Identity()
    {
        var eo = EtchedOracleFactory.Create(_alice);

        eo.Name.Should().Be("Etched Oracle");
        eo.ManaCost.Should().Be("{4}");
        eo.HasType(CardType.Artifact).Should().BeTrue();
        eo.HasType(CardType.Creature).Should().BeTrue();
        eo.Subtypes.Should().NotContain(CardSubtype.Human);
        eo.Subtypes.Should().Contain(CardSubtype.Wizard);
        eo.BasePower.Should().Be(0);
        eo.BaseToughness.Should().Be(0);
        eo.Owner.Should().BeSameAs(_alice);
        eo.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EtchedOracle_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Etched Oracle", _alice);

        card.Should().BeOfType<Creature>("Etched Oracle is an Artifact Creature");
        card.Name.Should().Be("Etched Oracle");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Sunburst",
                "Sunburst keyword marker surfaced");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Sunburst ETB trigger is surfaced for shape");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{1}, remove four +1/+1 counters: target player draws 3 is surfaced");
    }

    // -----------------------------------------------------------------------
    // Declared counter-removal cost (CR 118.3) — the +1/+1 removal is hoisted
    // from the resolve closure into the ability's declared cost list as an
    // AdditionalCost.RemoveCounters, so cost-validation / activation-legality
    // scans see it (additional-cost-remove-counters-primitive).
    // -----------------------------------------------------------------------

    [Fact]
    public void EtchedOracle_Activated_DeclaresRemoveFourPlusOnePlusOneCountersCost()
    {
        var eo = EtchedOracleFactory.Create(_alice);

        var ability = eo.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle()
            .Which.Cost.TotalValue.Should().Be(1, "the ability costs {1}");

        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.RemoveCounters)
            .Which.Should().Match<AdditionalCost>(c =>
                c.CounterType == CounterType.PlusOnePlusOne
                && c.CounterAmount == 4,
                "remove four +1/+1 counters is a declared cost, not inline in the resolve closure");
    }

    // -----------------------------------------------------------------------
    // Sunburst ETB (CR 702.44a — creature branch)
    // -----------------------------------------------------------------------

    [Fact]
    public void EtchedOracle_EtbWithThreeColorsPaid_AddsThreePlusOnePlusOneCounters()
    {
        var eo = EtchedOracleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(eo);
        eo.SetZone(ZoneType.Battlefield);

        eo.SetPendingCastColors(new[]
        {
            ManaColor.White, ManaColor.Blue, ManaColor.Red,
        });

        var etb = eo.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        eo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "three colors of mana spent → three +1/+1 counters (CR 702.44a)");
    }

    [Fact]
    public void EtchedOracle_EtbWithZeroColors_AddsNoCounters()
    {
        var eo = EtchedOracleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(eo);
        eo.SetZone(ZoneType.Battlefield);
        eo.SetPendingCastColors(Array.Empty<ManaColor>());

        var etb = eo.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        eo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Activated ability — {1}, Remove four +1/+1 counters: target player
    // draws three cards. The removal is paid as the DECLARED cost; the
    // resolve effect only performs the draw.
    // -----------------------------------------------------------------------

    [Fact]
    public void EtchedOracle_RemoveCountersCost_Paid_RemovesFourCounters()
    {
        var eo = EtchedOracleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(eo);
        eo.SetZone(ZoneType.Battlefield);
        eo.Counters.Add(CounterType.PlusOnePlusOne, 5);

        var ability = eo.Abilities.OfType<ActivatedAbility>().Single();
        var counterCost = ability.Costs.OfType<AdditionalCost>()
            .Single(c => c.CostType == AdditionalCostType.RemoveCounters);

        // Pay the declared counter-removal cost through the central seam.
        new CostPayment().PayCosts(_alice, new ICost[] { counterCost });

        eo.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "exactly the declared four +1/+1 counters were removed as the cost");
    }

    [Fact]
    public void EtchedOracle_RemoveCountersCost_CannotPay_WithFewerThanFour()
    {
        var eo = EtchedOracleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(eo);
        eo.SetZone(ZoneType.Battlefield);
        eo.Counters.Add(CounterType.PlusOnePlusOne, 3);

        var ability = eo.Abilities.OfType<ActivatedAbility>().Single();
        var counterCost = ability.Costs.OfType<AdditionalCost>()
            .Single(c => c.CostType == AdditionalCostType.RemoveCounters);

        counterCost.CanPay(_alice).Should().BeFalse(
            "three +1/+1 counters is fewer than the required four");
    }

    [Fact]
    public void EtchedOracle_Activated_NoTargetChosen_DrawsForController()
    {
        var eo = EtchedOracleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(eo);
        eo.SetZone(ZoneType.Battlefield);
        SeedLibrary(_alice, 10);

        var aliceBefore = _alice.Zones.Hand.GetCards().Count();

        // No target chosen (shape-test path) → controller (ctx.Controller) draws.
        var ability = eo.Abilities.OfType<ActivatedAbility>().Single();
        ContextResolve.Resolve(ability, _alice, _alice);

        (_alice.Zones.Hand.GetCards().Count() - aliceBefore).Should().Be(3,
            "with no target chosen, the controller draws three (no-target fallback)");
    }

    [Fact]
    public void EtchedOracle_Activated_TargetPlayerDrawsThree()
    {
        var eo = EtchedOracleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(eo);
        eo.SetZone(ZoneType.Battlefield);
        SeedLibrary(_alice, 10);
        SeedLibrary(_bob, 10);

        var aliceBefore = _alice.Zones.Hand.GetCards().Count();
        var bobBefore = _bob.Zones.Hand.GetCards().Count();

        // Choose Bob as the "target player".
        var ability = eo.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });
        ContextResolve.Resolve(ability, _alice, _alice, _bob);

        (_bob.Zones.Hand.GetCards().Count() - bobBefore).Should().Be(3,
            "the chosen target player draws three");
        (_alice.Zones.Hand.GetCards().Count() - aliceBefore).Should().Be(0,
            "the non-targeted player draws nothing");
    }
}
