using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>Unit tests for <see cref="WalkingBallistaFactory"/>.</summary>
public class WalkingBallistaTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity / types
    // -----------------------------------------------------------------------

    [Fact]
    public void WalkingBallista_IsCreatureAndArtifact()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        wb.HasType(CardType.Creature).Should().BeTrue("Walking Ballista is a Creature");
        wb.HasType(CardType.Artifact).Should().BeTrue("Walking Ballista is an Artifact");
    }

    [Fact]
    public void WalkingBallista_HasConstructSubtype()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        wb.HasSubtype(CardSubtype.Construct).Should().BeTrue();
    }

    [Fact]
    public void WalkingBallista_IsZeroZero()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        wb.BasePower.Should().Be(0);
        wb.BaseToughness.Should().Be(0);
    }

    [Fact]
    public void WalkingBallista_OwnerAndControllerAreSet()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        wb.Owner.Should().BeSameAs(_alice);
        wb.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability count / presence
    // -----------------------------------------------------------------------

    [Fact]
    public void WalkingBallista_HasExactlyTwoActivatedAbilities()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        wb.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Grow ability: {4}: Put a +1/+1 counter (sorcery-speed restriction deferred)
    // -----------------------------------------------------------------------

    [Fact]
    public void WalkingBallista_GrowAbility_RequiresFourGenericMana()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        var grow = wb.Abilities.OfType<ActivatedAbility>()
            .FirstOrDefault(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Cost.TotalValue >= 4));

        grow.Should().NotBeNull("the {4} grow ability should be present");
    }

    [Fact]
    public void WalkingBallista_GrowAbility_AddsOnePlusOnePlusOneCounter_OnResolve()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        var grow = wb.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Cost.TotalValue >= 4));

        grow.Resolve();

        wb.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Ping ability: Remove a +1/+1 counter: deal 1 damage to any target
    // -----------------------------------------------------------------------

    [Fact]
    public void WalkingBallista_PingAbility_HasRemoveCounterCost()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        var ping = wb.Abilities.OfType<ActivatedAbility>()
            .FirstOrDefault(a => a.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Any());

        ping.Should().NotBeNull("the ping ability should exist with a counter-removal cost");
    }

    [Fact]
    public void WalkingBallista_PingAbility_CannotPayWhenNoCounters()
    {
        var wb = WalkingBallistaFactory.Create(_alice);
        // starts at 0 counters

        var ping = wb.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Any());
        var cost = ping.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        cost.CanPay(_alice).Should().BeFalse("no counters to remove");
    }

    [Fact]
    public void WalkingBallista_PingAbility_CanPayWhenCounterPresent()
    {
        var wb = WalkingBallistaFactory.Create(_alice);
        wb.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var ping = wb.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Any());
        var cost = ping.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void WalkingBallista_PingAbility_RemovesOneCounterOnPay()
    {
        var wb = WalkingBallistaFactory.Create(_alice);
        wb.Counters.Add(CounterType.PlusOnePlusOne, 3);

        var ping = wb.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Any());
        var cost = ping.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        cost.Pay(_alice);

        wb.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    [Fact]
    public void WalkingBallista_PingAbility_ThrowsWhenCounterMissing()
    {
        var wb = WalkingBallistaFactory.Create(_alice);
        // no counters

        var ping = wb.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Any());
        var cost = ping.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        var act = () => cost.Pay(_alice);
        act.Should().Throw<InvalidOperationException>();
    }

    // -----------------------------------------------------------------------
    // ETB X +1/+1 counters: "enters the battlefield with X +1/+1 counters"
    // (CR 122.1g). X is read from PendingCastX, stamped by SpellCastFlow at
    // cast time — simulated here the same way the Hangarback / Endless One
    // tests do. Same PendingCastX → ETB-counter mechanism.
    // -----------------------------------------------------------------------

    [Fact]
    public void WalkingBallista_AttachesEtbTrigger()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        wb.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Walking Ballista has exactly one ETB (enters-with-X-counters) trigger");
    }

    [Fact]
    public void WalkingBallista_EtbWithXEquals3_GainsThreePlusOneCounters()
    {
        var wb = WalkingBallistaFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(wb);
        wb.SetZone(ZoneType.Battlefield);

        // SpellCastFlow stamps PendingCastX after ChooseXAsync; simulate.
        wb.SetPendingCastX(3);

        wb.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);

        var etb = wb.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        // 0/0 base + three +1/+1 counters → a 3/3 once the Layer-7 P/T
        // system folds them in (CR 122.1c). Counter count is the harness-
        // observable state without a wired ContinuousEffectsService (same
        // assertion shape as Hangarback / Endless One).
        wb.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "Walking Ballista enters with X (=3) +1/+1 counters per CR 122.1g → 3/3");
        wb.BasePower.Should().Be(0, "base P/T is unchanged; counters add via Layer 7");
        wb.BaseToughness.Should().Be(0);
        wb.PendingCastX.Should().BeNull(
            "PendingCastX stamp consumed once the ETB effect reads it — re-entries don't double-count");
    }

    [Fact]
    public void WalkingBallista_EtbWithXEquals0_NoCountersPlaced_StaysZeroZero()
    {
        var wb = WalkingBallistaFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(wb);
        wb.SetZone(ZoneType.Battlefield);
        wb.SetPendingCastX(0);

        var etb = wb.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        wb.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "X=0 → zero counters placed → 0/0 SBA-fodder (dies to CR 704.5f)");
        wb.BasePower.Should().Be(0);
        wb.BaseToughness.Should().Be(0);
        wb.PendingCastX.Should().BeNull("PendingCastX cleared regardless of X value");
    }

    [Fact]
    public void WalkingBallista_NoPendingX_NonCastEntry_NoCountersPlaced()
    {
        // Non-cast entries (blink, copy, etc.) leave PendingCastX = null —
        // the ETB effect must no-op rather than throw or place arbitrary
        // counters.
        var wb = WalkingBallistaFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(wb);
        wb.SetZone(ZoneType.Battlefield);
        wb.PendingCastX.Should().BeNull();

        var etb = wb.Abilities.OfType<TriggeredAbility>().Single();
        var act = () => { foreach (var e in etb.Effects) e.Execute(); };
        act.Should().NotThrow();

        wb.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "non-cast entry leaves Walking Ballista as a 0/0 with no counters (CR 122.1g — no chosen X)");
    }

    [Fact]
    public void WalkingBallista_RoutesThroughCountersService_HardenedScalesBumpsApply()
    {
        // Hardened Scales rewrites +1/+1 counter placements via a
        // ReplacementBus subscriber. Wire WB with a replacement bus that
        // bumps every PlusOnePlusOne placement by 1, then cast for X=2.
        // Expected: 2 + 1 = 3 counters land.
        var bus = new ReplacementBus();
        bus.Register(new LambdaReplacement<CounterAddIntent>(
            applies: (intent, _) => intent.Type == CounterType.PlusOnePlusOne,
            replace: (intent, _) => intent with { Amount = intent.Amount + 1 }));

        var wb = WalkingBallistaFactory.Create(_alice, replacements: bus);
        _alice.Zones.Battlefield.AddCard(wb);
        wb.SetZone(ZoneType.Battlefield);
        wb.SetPendingCastX(2);

        var etb = wb.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        wb.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "Hardened Scales (+1 replacement on PlusOnePlusOne placements) bumps the count via CountersService.Add");
    }

    // -----------------------------------------------------------------------
    // Assaultron Invader — byte-for-byte functional reprint (Fallout / PIP),
    // served by the same factory via the shared CardDefinition. The ETB-X
    // counters fix flows for free because the reprint routes through the
    // same BuildWithEtbCounters path.
    // -----------------------------------------------------------------------

    [Fact]
    public void AssaultronInvader_DispatchesViaNamedCardFactory_SameShape()
    {
        var card = NamedCardFactory.Create(
            WalkingBallistaFactory.AssaultronInvaderCardName, _alice);

        card.Name.Should().Be(WalkingBallistaFactory.AssaultronInvaderCardName);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Assaultron Invader carries the same ETB-counters trigger as Walking Ballista");
    }

    [Fact]
    public void AssaultronInvader_EtbWithXEquals3_GainsThreePlusOneCounters()
    {
        var ai = WalkingBallistaFactory.Create(
            _alice, WalkingBallistaFactory.AssaultronInvaderCardName);

        ai.Name.Should().Be(WalkingBallistaFactory.AssaultronInvaderCardName);

        _alice.Zones.Battlefield.AddCard(ai);
        ai.SetZone(ZoneType.Battlefield);
        ai.SetPendingCastX(3);

        var etb = ai.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        ai.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "the reprint enters with X (=3) +1/+1 counters via the same factory path → 3/3");
        ai.BasePower.Should().Be(0);
        ai.BaseToughness.Should().Be(0);
        ai.PendingCastX.Should().BeNull();
    }
}
