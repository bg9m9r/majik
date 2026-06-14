using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// STAGE 1 (re-sourceable abilities) — groundwork for Agatha's Soul Cauldron
/// granting an imprinted creature's activated abilities re-homed to a bearer.
///
/// <para>
/// Two additive seams are exercised here: (1) <see cref="ResolutionContext.Source"/>
/// is populated from the resolving ability's own source, so an effect can read
/// "its source" generically (CR 113.7); and (2)
/// <see cref="ActivatedAbility.RebindTo"/> now re-homes source-capturing COSTS
/// ({T} / sacrifice) onto the new source, so a re-sourced ability taps /
/// sacrifices the NEW permanent rather than the original (CR 707.2). Effects
/// are NOT migrated this stage.
/// </para>
/// </summary>
public class ActivatedAbilityRebindSourceTests
{
    private readonly Player _alice = new("Alice", 20);

    /// <summary>Test effect that records the <see cref="ResolutionContext.Source"/>
    /// it was resolved with, proving the ability threads its own source.</summary>
    private sealed class SourceCapturingEffect : IEffect
    {
        public Permanent? SeenSource { get; private set; }
        public bool Ran { get; private set; }
        public string Description => "capture source";

        public ValueTask ExecuteAsync(ResolutionContext ctx)
        {
            Ran = true;
            SeenSource = ctx.Source;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task ResolveAsync_ExposesAbilitySource_InContext()
    {
        // Arrange — an activated ability whose source is permanent A.
        var source = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = _alice };
        source.SetZone(ZoneType.Battlefield);
        var effect = new SourceCapturingEffect();
        var ability = new ActivatedAbility(
            source: source,
            controller: _alice,
            effects: new[] { effect });

        // Act
        await ability.ResolveAsync(agent: null, game: null);

        // Assert — the effect saw the ability's own source on the context.
        effect.Ran.Should().BeTrue();
        effect.SeenSource.Should().BeSameAs(source);
    }

    [Fact]
    public void RebindTo_TapCost_PaysWithNewSource_NotOriginal()
    {
        // Arrange — ability on A with a {T} cost capturing A.
        var a = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = _alice };
        a.SetZone(ZoneType.Battlefield);
        a.ClearSummoningSickness();

        var b = new Creature("Llanowar Elves", "G", 1, 1) { Controller = _alice };
        b.SetZone(ZoneType.Battlefield);
        b.ClearSummoningSickness();

        var ability = new ActivatedAbility(
            source: a,
            controller: _alice,
            costs: new[] { AdditionalCost.Tap(a) });

        // Act — re-source the ability onto B, then pay the rebound costs.
        var rebound = ability.RebindTo(b, _alice);
        foreach (var cost in rebound.Costs)
        {
            cost.Pay(_alice);
        }

        // Assert — the rebound {T} cost taps B (the new source), not A.
        rebound.Source.Should().BeSameAs(b);
        b.IsTapped.Should().BeTrue();
        a.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void RebindTo_SacrificeCost_PaysWithNewSource_NotOriginal()
    {
        // Arrange — ability on A with a sacrifice cost capturing A.
        var a = new Creature("Grizzly Bears", "1G", 2, 2);
        a.SetOwner(_alice);
        a.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);

        var b = new Creature("Llanowar Elves", "G", 1, 1);
        b.SetOwner(_alice);
        b.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(b);
        b.SetZone(ZoneType.Battlefield);

        var ability = new ActivatedAbility(
            source: a,
            controller: _alice,
            costs: new[] { AdditionalCost.Sacrifice(a) });

        // Act
        var rebound = ability.RebindTo(b, _alice);
        foreach (var cost in rebound.Costs)
        {
            cost.Pay(_alice);
        }

        // Assert — the rebound sacrifice cost sacrifices B, not A.
        b.Zone.Should().Be(ZoneType.Graveyard);
        a.Zone.Should().Be(ZoneType.Battlefield);
    }

    // ----------------------------------------------------------------------
    // STAGE 1 — counter-payment costs (the agatha-counter-cost-rebind-seam).
    // RemovePlusOnePlusOneCounterCost / RemoveChargeCounterCost / AddCounterCost
    // are bare ICosts that capture their source permanent. RebindTo must now
    // re-home them via IRebindableCost so a re-sourced counter-paying ability
    // removes / adds the counter on the BEARER, not the original (CR 707.2).
    // ----------------------------------------------------------------------

    [Fact]
    public void RebindTo_RemovePlusOnePlusOneCounterCost_PaysFromNewSource_NotOriginal()
    {
        // Arrange — A and B both carry a +1/+1 counter; the ability's cost
        // removes a +1/+1 counter from A.
        var a = new Creature("Walking Ballista", "0", 0, 0) { Controller = _alice };
        a.SetZone(ZoneType.Battlefield);
        a.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var b = new Creature("Arcbound Worker", "1", 0, 0) { Controller = _alice };
        b.SetZone(ZoneType.Battlefield);
        b.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var ability = new ActivatedAbility(
            source: a,
            controller: _alice,
            costs: new ICost[] { new RemovePlusOnePlusOneCounterCost(a, 1) });

        // Act — re-source onto B, then pay the rebound costs.
        var rebound = ability.RebindTo(b, _alice);
        foreach (var cost in rebound.Costs)
        {
            cost.Pay(_alice);
        }

        // Assert — the counter came off B (the new source), not A.
        rebound.Source.Should().BeSameAs(b);
        b.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        a.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void RebindTo_RemoveChargeCounterCost_PaysFromNewSource_NotOriginal()
    {
        var a = new Artifact("Coretapper", "2") { Controller = _alice };
        a.SetZone(ZoneType.Battlefield);
        a.Counters.Add(CounterType.Charge, 1);

        var b = new Artifact("Pentad Prism", "2") { Controller = _alice };
        b.SetZone(ZoneType.Battlefield);
        b.Counters.Add(CounterType.Charge, 1);

        var ability = new ActivatedAbility(
            source: a,
            controller: _alice,
            costs: new ICost[] { new RemoveChargeCounterCost(a, 1) });

        var rebound = ability.RebindTo(b, _alice);
        foreach (var cost in rebound.Costs)
        {
            cost.Pay(_alice);
        }

        rebound.Source.Should().BeSameAs(b);
        b.Counters.Count(CounterType.Charge).Should().Be(0);
        a.Counters.Count(CounterType.Charge).Should().Be(1);
    }

    [Fact]
    public void RebindTo_AddCounterCost_PutsCounterOnNewSource_NotOriginal()
    {
        // Devoted Druid's "Put a -1/-1 counter on it" untap cost.
        var a = new Creature("Devoted Druid", "1G", 0, 2) { Controller = _alice };
        a.SetZone(ZoneType.Battlefield);

        var b = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = _alice };
        b.SetZone(ZoneType.Battlefield);

        var ability = new ActivatedAbility(
            source: a,
            controller: _alice,
            costs: new ICost[]
            {
                new AddCounterCost(a, CounterType.MinusOneMinusOne, 1),
            });

        var rebound = ability.RebindTo(b, _alice);
        foreach (var cost in rebound.Costs)
        {
            cost.Pay(_alice);
        }

        // Assert — the -1/-1 counter landed on B (the new source), not A.
        rebound.Source.Should().BeSameAs(b);
        b.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);
        a.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0);
    }

    [Fact]
    public void RebindTo_NonMatchingCounterCost_PassesThroughUnchanged()
    {
        // A counter cost that captures a THIRD permanent (not the ability's
        // own source) must NOT be re-homed — RebindTo only swaps the cost whose
        // captured source IS the ability's old source.
        var a = new Creature("Devoted Druid", "1G", 0, 2) { Controller = _alice };
        a.SetZone(ZoneType.Battlefield);
        var other = new Creature("Llanowar Elves", "G", 1, 1) { Controller = _alice };
        other.SetZone(ZoneType.Battlefield);
        other.Counters.Add(CounterType.PlusOnePlusOne, 1);
        var b = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = _alice };
        b.SetZone(ZoneType.Battlefield);

        var ability = new ActivatedAbility(
            source: a,
            controller: _alice,
            costs: new ICost[] { new RemovePlusOnePlusOneCounterCost(other, 1) });

        var rebound = ability.RebindTo(b, _alice);
        foreach (var cost in rebound.Costs)
        {
            cost.Pay(_alice);
        }

        // The counter still came off `other`, not B (which has none anyway).
        other.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        b.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    // ----------------------------------------------------------------------
    // UNBLOCKED CARDS — the full real abilities of Devoted Druid / Spike Feeder
    // re-home end-to-end (cost + effect on the bearer) once the card is
    // RebindSafe and its counter cost re-homes. This is the Agatha's Soul
    // Cauldron grant payoff (CR 613.1f / 702.49 imprint, CR 707.2 re-source).
    // ----------------------------------------------------------------------

    [Fact]
    public async Task DevotedDruid_UntapAbility_RebindsToBearer_PaysAndUntapsBearer()
    {
        // Arrange — the real Devoted Druid untap ability, re-homed onto a
        // bearer (as Agatha's Soul Cauldron would grant it).
        var druid = DevotedDruidFactory.Create(_alice);
        druid.SetZone(ZoneType.Battlefield);

        var bearer = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = _alice };
        bearer.SetZone(ZoneType.Battlefield);
        bearer.Tap(); // tapped so we can observe the untap.

        // The untap ability is the non-mana activated ability.
        var untap = druid.Abilities
            .OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);
        untap.RebindSafe.Should().BeTrue();

        var rebound = untap.RebindTo(bearer, _alice);

        // Act — pay the rebound cost (puts the -1/-1 counter), then resolve.
        foreach (var cost in rebound.Costs) cost.Pay(_alice);
        await rebound.ResolveAsync(agent: null, game: null);

        // Assert — the -1/-1 counter landed on the BEARER and the BEARER untapped;
        // the original Druid is untouched.
        bearer.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);
        bearer.IsTapped.Should().BeFalse();
        druid.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0);
    }

    [Fact]
    public async Task SpikeFeeder_GainLifeAbility_RebindsToBearer_SpendsBearerCounter()
    {
        // Arrange — the real Spike Feeder lifegain ability, re-homed onto a
        // bearer that carries a +1/+1 counter.
        var feeder = SpikeFeederFactory.Create(_alice);
        feeder.SetZone(ZoneType.Battlefield);

        var bearer = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = _alice };
        bearer.SetZone(ZoneType.Battlefield);
        bearer.Counters.Add(CounterType.PlusOnePlusOne, 1);

        // The free lifegain ability: no mana cost, one counter cost, no targets.
        var gain = feeder.Abilities
            .OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility
                && a.TargetRequests.Count == 0
                && a.Costs.OfType<ManaCostCost>().Any() == false);
        gain.RebindSafe.Should().BeTrue();

        var lifeBefore = _alice.LifeTotal;
        var rebound = gain.RebindTo(bearer, _alice);

        // Act
        foreach (var cost in rebound.Costs) cost.Pay(_alice);
        await rebound.ResolveAsync(agent: null, game: null);

        // Assert — the +1/+1 counter came off the BEARER (not the original
        // feeder), and the controller gained 2 life.
        bearer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        _alice.LifeTotal.Should().Be(lifeBefore + SpikeFeederFactory.LifeGained);
    }
}
