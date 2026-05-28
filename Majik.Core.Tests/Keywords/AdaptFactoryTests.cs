using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// CR 702.116 — Adapt keyword tests. Covers:
/// <list type="bullet">
///   <item><see cref="AdaptFactory.Build"/> returns an activated ability
///       with the printed mana cost.</item>
///   <item><see cref="KeywordAbility"/> marker "Adapt N" is stamped on
///       the source.</item>
///   <item>Resolving Adapt N on a creature with no +1/+1 counters places
///       N +1/+1 counters.</item>
///   <item>Resolving Adapt N on a creature with EXISTING +1/+1 counters
///       is a no-op (CR 702.116b — "if it has no +1/+1 counters").</item>
///   <item>Adapt routes through <see cref="Services.CountersService.Add"/>
///       so the post-commit <see cref="CounterAddedEvent"/> is published
///       (surface for Emperor of Bones's +1/+1-counter trigger).</item>
///   <item>Argument validation (null source, empty cost, non-positive N).</item>
/// </list>
/// </summary>
public class AdaptFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Creature MakeSimicHybrid()
    {
        var c = new Creature("Simic Hybrid", "{1}{G}{U}", 1, 1)
        {
            Owner = _alice,
            Controller = _alice,
        };
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);
        return c;
    }

    [Fact]
    public void Build_ReturnsActivatedAbility_WithProvidedManaCost()
    {
        var hybrid = MakeSimicHybrid();
        var ability = AdaptFactory.Build(hybrid, "{1}{B}", 2);

        ability.Should().NotBeNull();
        ability.Costs.Should().ContainSingle()
            .Which.Should().BeOfType<ManaCostCost>();
    }

    [Fact]
    public void Build_StampsKeywordMarker_AdaptN_OnSource()
    {
        var hybrid = MakeSimicHybrid();
        AdaptFactory.Build(hybrid, "{1}{B}", 2);

        hybrid.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Adapt 2");
    }

    [Fact]
    public void Adapt_OnCreatureWithZeroPlusOnePlusOneCounters_PlacesNCounters()
    {
        var hybrid = MakeSimicHybrid();
        var ability = AdaptFactory.Build(hybrid, "{1}{B}", 2);

        // Resolve the activated ability's effects directly (cost-pay path
        // is exercised elsewhere — this test isolates the effect body).
        foreach (var eff in ability.Effects) eff.Execute();

        hybrid.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    [Fact]
    public void Adapt_OnCreatureWithExistingPlusOnePlusOneCounters_IsNoOp()
    {
        // CR 702.116b — "if it has no +1/+1 counters on it". A re-Adapt
        // on a creature that already has +1/+1 counters succeeds at
        // activation but adds no counters at resolution.
        var hybrid = MakeSimicHybrid();
        hybrid.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var ability = AdaptFactory.Build(hybrid, "{1}{B}", 2);
        foreach (var eff in ability.Effects) eff.Execute();

        hybrid.Counters.Count(CounterType.PlusOnePlusOne)
            .Should().Be(1, because: "Adapt fizzles on already-counter'd creatures");
    }

    [Fact]
    public void Adapt_RoutesThroughCountersService_PublishesCounterAddedEvent()
    {
        var hybrid = MakeSimicHybrid();
        var bus = new EventBus();
        var fired = new List<CounterAddedEvent>();
        bus.Subscribe<CounterAddedEvent>(e => fired.Add(e));

        var ability = AdaptFactory.Build(
            hybrid, "{1}{B}", 2, replacements: null, eventBus: bus);
        foreach (var eff in ability.Effects) eff.Execute();

        fired.Should().ContainSingle();
        fired[0].Target.Should().BeSameAs(hybrid);
        fired[0].CounterType.Should().Be(CounterType.PlusOnePlusOne);
        fired[0].Amount.Should().Be(2);
    }

    [Fact]
    public void Adapt_NotOnBattlefield_IsNoOp()
    {
        // CR 702.116a — activated abilities of permanents are only
        // functional from the battlefield (CR 113.6). A creature that
        // somehow loses the battlefield zone between activation and
        // resolution doesn't place counters.
        var hybrid = MakeSimicHybrid();
        var ability = AdaptFactory.Build(hybrid, "{1}{B}", 2);

        hybrid.SetZone(ZoneType.Graveyard);
        foreach (var eff in ability.Effects) eff.Execute();

        hybrid.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void Build_NullSource_Throws()
    {
        Action act = () => AdaptFactory.Build(null!, "{1}{B}", 2);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_EmptyCost_Throws()
    {
        var hybrid = MakeSimicHybrid();
        Action act = () => AdaptFactory.Build(hybrid, "", 2);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_NonPositiveN_Throws()
    {
        var hybrid = MakeSimicHybrid();
        Action act = () => AdaptFactory.Build(hybrid, "{1}{B}", 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
