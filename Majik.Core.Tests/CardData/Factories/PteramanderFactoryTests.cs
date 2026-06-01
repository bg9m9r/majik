using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="PteramanderFactory"/>.
///
/// Card: Pteramander (Ravnica Allegiance, {U}). Creature — Salamander
/// Drake. 1/1.
///
/// Oracle text (Scryfall-verified):
/// <list type="number">
///   <item>"Flying"</item>
///   <item>"{7}{U}: Adapt 4. This ability costs {1} less to activate for
///       each instant and sorcery card in your graveyard. (If this creature
///       has no +1/+1 counters on it, put four +1/+1 counters on it.)"</item>
/// </list>
///
/// Coverage:
/// <list type="bullet">
///   <item>Identity ({U}, 1/1, Creature, Salamander, Drake, Flying).</item>
///   <item>Dispatch via <see cref="NamedCardFactory"/>.</item>
///   <item>Ability shape — Flying + Adapt 4 keyword markers, one activated
///       Adapt ability whose mana cost is the graveyard-reducing
///       <see cref="PteramanderFactory.GraveyardReducedManaCost"/>.</item>
///   <item>Cost reduction (CR 118.5 / 117.7c) — {1} less generic per
///       instant/sorcery in the controller's graveyard, floored at zero,
///       blue pip preserved.</item>
///   <item>Adapt 4 resolution — 4 +1/+1 counters when none present, no-op
///       when already present (CR 702.116b).</item>
///   <item>Reduced cost is actually payable from a reduced pool.</item>
/// </list>
/// </summary>
public class PteramanderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Instant MakeInstant(string name = "Test Instant")
    {
        var c = new Instant(name, "{U}") { Owner = _alice };
        c.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(c);
        return c;
    }

    private Sorcery MakeSorcery(string name = "Test Sorcery")
    {
        var c = new Sorcery(name, "{U}") { Owner = _alice };
        c.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(c);
        return c;
    }

    [Fact]
    public void Pteramander_Identity()
    {
        var p = PteramanderFactory.Create(_alice);

        p.Name.Should().Be("Pteramander");
        p.ManaCost.Should().Be("{U}");
        p.BasePower.Should().Be(1);
        p.BaseToughness.Should().Be(1);
        p.HasType(CardType.Creature).Should().BeTrue();
        p.Subtypes.Should().Contain(CardSubtype.Salamander);
        p.Subtypes.Should().Contain(CardSubtype.Drake);
        p.Owner.Should().BeSameAs(_alice);
        p.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Pteramander_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Pteramander", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Pteramander");
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void Pteramander_HasFlyingMarker()
    {
        var p = PteramanderFactory.Create(_alice);

        p.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Flying");
    }

    [Fact]
    public void Pteramander_AbilityShape()
    {
        var p = PteramanderFactory.Create(_alice);

        // One activated ability (Adapt 4).
        p.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);

        // Adapt keyword marker stamped by AdaptFactory.
        p.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Adapt 4");

        // The activated ability's mana cost is the graveyard-reducing cost.
        var adapt = p.Abilities.OfType<ActivatedAbility>().Single();
        adapt.Costs.OfType<PteramanderFactory.GraveyardReducedManaCost>()
            .Should().ContainSingle();
    }

    [Fact]
    public void CostReduction_NoGraveyardInstantsOrSorceries_ChargesFullSevenU()
    {
        var p = PteramanderFactory.Create(_alice);
        var cost = ActivatedManaCost(p);

        cost.Reduction().Should().Be(0);
        cost.Effective().Should().Be(ManaCost.Parse("{7}{U}"));
    }

    [Fact]
    public void CostReduction_ThreeInstantSorceryCards_ReducesGenericByThree()
    {
        var p = PteramanderFactory.Create(_alice);
        MakeInstant("i1");
        MakeInstant("i2");
        MakeSorcery("s1");

        var cost = ActivatedManaCost(p);

        cost.Reduction().Should().Be(3);
        cost.Effective().Should().Be(ManaCost.Parse("{4}{U}"),
            because: "CR 117.7c — only generic is reduced; the {U} pip is preserved");
    }

    [Fact]
    public void CostReduction_SevenInstantSorceryCards_ReducesToJustBluePip()
    {
        var p = PteramanderFactory.Create(_alice);
        for (var i = 0; i < 5; i++) MakeInstant($"i{i}");
        for (var i = 0; i < 2; i++) MakeSorcery($"s{i}");

        var cost = ActivatedManaCost(p);

        cost.Reduction().Should().Be(7);
        cost.Effective().Should().Be(ManaCost.Parse("{U}"));
    }

    [Fact]
    public void CostReduction_MoreThanSeven_FloorsGenericAtZero_NeverTouchesBluePip()
    {
        var p = PteramanderFactory.Create(_alice);
        for (var i = 0; i < 10; i++) MakeInstant($"i{i}");

        var cost = ActivatedManaCost(p);

        cost.Reduction().Should().Be(10);
        cost.Effective().Should().Be(ManaCost.Parse("{U}"),
            because: "CR 118.5 — generic floors at zero; the {U} pip is never reduced");
    }

    [Fact]
    public void CostReduction_IgnoresNonInstantSorceryGraveyardCards()
    {
        var p = PteramanderFactory.Create(_alice);
        // A creature card in the graveyard does not count.
        var beast = new Creature("Test Beast", "{G}", 2, 2) { Owner = _alice };
        beast.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(beast);
        MakeInstant("i1");

        var cost = ActivatedManaCost(p);

        cost.Reduction().Should().Be(1, because: "only instant and sorcery cards count");
        cost.Effective().Should().Be(ManaCost.Parse("{6}{U}"));
    }

    [Fact]
    public void ReducedCost_IsPayable_FromExactlyTheReducedPool()
    {
        var p = PteramanderFactory.Create(_alice);
        MakeInstant("i1");
        MakeInstant("i2");
        MakeInstant("i3"); // 3 cards → cost is {4}{U}

        var cost = ActivatedManaCost(p);

        // Empty pool cannot pay.
        cost.CanPay(_alice).Should().BeFalse();

        // Add exactly {4}{U}.
        _alice.AddManaToPool(ManaCost.Parse("{4}{U}"));
        cost.CanPay(_alice).Should().BeTrue();

        cost.Pay(_alice);
        _alice.ManaPool.CanPay(ManaCost.Parse("{1}")).Should().BeFalse(
            because: "the reduced cost consumed the whole {4}{U} pool");
    }

    [Fact]
    public void AdaptFour_PlacesFourCounters_WhenNonePresent()
    {
        var p = PteramanderFactory.Create(_alice);
        p.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(p);

        var adapt = p.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in adapt.Effects) eff.Execute();

        p.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(4);
    }

    [Fact]
    public void AdaptFour_IsNoOp_WhenPlusOneCountersAlreadyPresent()
    {
        var p = PteramanderFactory.Create(_alice);
        p.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(p);
        p.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var adapt = p.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in adapt.Effects) eff.Execute();

        p.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            because: "CR 702.116b — Adapt fizzles when +1/+1 counters already present");
    }

    private static PteramanderFactory.GraveyardReducedManaCost ActivatedManaCost(Creature p) =>
        p.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<PteramanderFactory.GraveyardReducedManaCost>().Single();
}
