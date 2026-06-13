using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
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
    // ETB X +1/+1 counters: "[this creature] enters with X +1/+1 counters on
    // it" (CR 614.1d / CR 202.3b). This is NOT a factory-attached ETB trigger.
    // The factory deliberately leaves it to the generic
    // EntersWithCountersBinder, which on the prod deck-build (DeckCardBuilder
    // APPROACH B → OverlayAdditiveBinders) registers a variable-X
    // EntersWithCountersReplacement that reads PendingCastX and places the
    // counters AS the permanent enters. These tests exercise that exact prod
    // mechanism: build the factory card, run the binder against its real
    // oracle text, then move it onto the battlefield through ZoneService and
    // assert the counters landed.
    //
    // Earlier this card self-managed via an ETB trigger + the
    // MarkSelfManagesEntersWithCounters flag; that produced ZERO counters on
    // the Approach-B route (the trigger was never registered with a live
    // TriggerManager and the flag suppressed the binder). The factory must NOT
    // attach an ETB trigger NOR self-manage — both regression-guarded here.
    // -----------------------------------------------------------------------

    private static CardEntity BallistaEntity(string name = WalkingBallistaFactory.CardName) =>
        new EmbeddedCardRepository().GetByName(name)!;

    [Fact]
    public void WalkingBallista_DoesNotAttachEtbTrigger()
    {
        // CR 614.1d — the ETB counters are a replacement registered by the
        // binder, NOT a factory-attached TriggeredAbility. Self-managing via a
        // trigger was the bug: the prod Approach-B route never registers it.
        var wb = WalkingBallistaFactory.Create(_alice);

        wb.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Walking Ballista's ETB counters are a binder-registered replacement, " +
            "not a self-managed ETB trigger");
    }

    [Fact]
    public void WalkingBallista_DoesNotSelfManageEntersWithCounters()
    {
        // The factory must leave SelfManagesEntersWithCounters false so the
        // EntersWithCountersBinder DOES register the variable-X replacement on
        // the prod route. Setting the flag suppresses the binder → 0 counters.
        var wb = WalkingBallistaFactory.Create(_alice);

        wb.SelfManagesEntersWithCounters.Should().BeFalse(
            "the binder owns the ETB-X replacement; self-managing suppresses it " +
            "and yields zero counters on the Approach-B prod route");
    }

    [Fact]
    public void WalkingBallista_BinderReplacement_EntersWithXEquals3_Counters()
    {
        // The prod mechanism: factory build + binder (reads the card's real
        // oracle text) + ZoneService move. X = 3 (cast {3}{3}).
        var bus = new ReplacementBus();
        var wb = WalkingBallistaFactory.Create(_alice);

        EntersWithCountersBinder.Bind(wb, BallistaEntity(), bus).Should().BeTrue(
            "the binder matches 'enters with X +1/+1 counters on it' and registers " +
            "the variable-X replacement");

        wb.SetOwner(_alice);
        wb.SetController(_alice);
        _alice.Zones.Library.AddCard(wb);
        wb.SetZone(ZoneType.Library);
        wb.SetPendingCastX(3);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(wb, ZoneType.Library, ZoneType.Battlefield, _alice);

        wb.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "Walking Ballista enters WITH X (=3) +1/+1 counters per CR 614.1d → 3/3");
        wb.BasePower.Should().Be(0, "base P/T is unchanged; counters add via Layer 7");
        wb.BaseToughness.Should().Be(0);
    }

    [Fact]
    public void WalkingBallista_BinderReplacement_ZeroX_NoCounters()
    {
        // No PendingCastX stamp → X = 0 → a 0/0 the SBA layer sends to the
        // graveyard (CR 704.5f). Non-cast entries (blink, copy) take this path.
        var bus = new ReplacementBus();
        var wb = WalkingBallistaFactory.Create(_alice);

        EntersWithCountersBinder.Bind(wb, BallistaEntity(), bus).Should().BeTrue();

        wb.SetOwner(_alice);
        wb.SetController(_alice);
        _alice.Zones.Library.AddCard(wb);
        wb.SetZone(ZoneType.Library);
        // No SetPendingCastX → X defaults to 0.

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(wb, ZoneType.Library, ZoneType.Battlefield, _alice);

        wb.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "X = 0 → zero counters placed → 0/0 SBA-fodder (CR 704.5f)");
    }

    [Fact]
    public void WalkingBallista_BinderReplacement_HardenedScalesBumpsApply()
    {
        // Hardened Scales bumps the +1/+1 counters AS they enter — it observes
        // the same ZoneMoveIntent.PlusOneCountersOnEnter channel the ETB-X
        // replacement stamps (CR 614). Wire a +1 bump on that channel, cast for
        // X = 2, expect 3 counters.
        var bus = new ReplacementBus();
        bus.Register(new LambdaReplacement<ZoneMoveIntent>(
            applies: (intent, _) => intent.ToZone == ZoneType.Battlefield
                                    && intent.PlusOneCountersOnEnter >= 1,
            replace: (intent, _) => intent with
            {
                PlusOneCountersOnEnter = intent.PlusOneCountersOnEnter + 1,
            }));

        var wb = WalkingBallistaFactory.Create(_alice);
        EntersWithCountersBinder.Bind(wb, BallistaEntity(), bus).Should().BeTrue();

        wb.SetOwner(_alice);
        wb.SetController(_alice);
        _alice.Zones.Library.AddCard(wb);
        wb.SetZone(ZoneType.Library);
        wb.SetPendingCastX(2);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(wb, ZoneType.Library, ZoneType.Battlefield, _alice);

        wb.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "Hardened Scales (+1 on the ETB +1/+1 intent channel) bumps X (=2) → 3");
    }

    // -----------------------------------------------------------------------
    // Assaultron Invader — byte-for-byte functional reprint (Fallout / PIP),
    // served by the same factory via the shared CardDefinition. The ETB-X
    // counters flow through the same generic binder mechanism, keyed on the
    // shared oracle text. (Assaultron is not in the embedded Modern pool, so
    // it can never reach the prod deck-build route; its ETB-X is exercised here
    // against Walking Ballista's equivalent oracle text.)
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
        card.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Assaultron Invader carries no self-managed ETB trigger, same as Walking Ballista");
    }

    [Fact]
    public void AssaultronInvader_BinderReplacement_EntersWithXEquals3_Counters()
    {
        var bus = new ReplacementBus();
        var ai = WalkingBallistaFactory.Create(
            _alice, WalkingBallistaFactory.AssaultronInvaderCardName);

        ai.Name.Should().Be(WalkingBallistaFactory.AssaultronInvaderCardName);
        ai.SelfManagesEntersWithCounters.Should().BeFalse(
            "the reprint also defers ETB-X to the binder");

        // Functional reprint → identical oracle text. Bind with Walking
        // Ballista's entity (same "enters with X +1/+1 counters on it" text).
        var entity = BallistaEntity();
        EntersWithCountersBinder.Bind(ai, entity, bus).Should().BeTrue();

        ai.SetOwner(_alice);
        ai.SetController(_alice);
        _alice.Zones.Library.AddCard(ai);
        ai.SetZone(ZoneType.Library);
        ai.SetPendingCastX(3);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(ai, ZoneType.Library, ZoneType.Battlefield, _alice);

        ai.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "the reprint enters with X (=3) +1/+1 counters via the same binder replacement → 3/3");
        ai.BasePower.Should().Be(0);
        ai.BaseToughness.Should().Be(0);
    }
}
