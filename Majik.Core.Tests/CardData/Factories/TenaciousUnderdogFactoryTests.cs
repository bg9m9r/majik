using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TenaciousUnderdogFactory"/> (Streets of New
/// Capenna, {1}{B}). Creature — Human Warrior 3/2.
///
/// Oracle text:
///   "Blitz—{2}{B}{B}, Pay 2 life. (If you cast this spell for its blitz cost,
///    it gains haste and "When this creature dies, draw a card." Sacrifice it
///    at the beginning of the next end step.)
///    You may cast this card from your graveyard using its blitz ability."
///
/// Exercises the new Blitz keyword subsystem (CR 702.152): identity, the
/// graveyard blitz alt-cost + bundled pay-2-life additional cost, the
/// BlitzWasPaid-gated dies-draw + delayed end-step sacrifice riders, and that
/// a normal-cast copy (BlitzWasPaid == false) gets none of the riders.
/// </summary>
[Trait("Color", "B")]
public class TenaciousUnderdogFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Identity()
    {
        var c = TenaciousUnderdogFactory.Create(_alice);

        c.Name.Should().Be("Tenacious Underdog");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HasHasteMarker()
    {
        var c = TenaciousUnderdogFactory.Create(_alice);
        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Haste", "CR 702.152b — blitz creatures gain haste");
    }

    [Fact]
    public void BuildBlitzCost_GraveyardAltCost_AndPay2LifeAdditional()
    {
        var (alt, life) = TenaciousUnderdogFactory.BuildBlitzCost();

        alt.AlternativeManaCost.Should().Be(ManaCost.Parse("2BB"));
        alt.SourceZone.Should().Be(ZoneType.Graveyard);
        life.Amount.Should().Be(2);
    }

    [Fact]
    public void BlitzAltCost_LegalFromGraveyard()
    {
        var c = TenaciousUnderdogFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);

        var (alt, _) = TenaciousUnderdogFactory.BuildBlitzCost();
        alt.CanCastFor(c, _alice).Should().BeTrue();
    }

    [Fact]
    public void DiesTrigger_GatedOnBlitzWasPaid_NormalCastDoesNotMatch()
    {
        var c = TenaciousUnderdogFactory.Create(_alice);
        var dies = DiesTrigger(c);

        // Normal cast: BlitzWasPaid stays false → intervening-if is false, so
        // the dies-draw rider does not fire (CR 702.152c).
        dies.InterveningIf!().Should().BeFalse();

        c.BlitzWasPaid = true;
        dies.InterveningIf!().Should().BeTrue();
    }

    [Fact]
    public void EtbRider_BlitzPaid_GrantsHaste_AndRegistersDelayedSacrifice()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var c = TenaciousUnderdogFactory.Create(_alice, triggers, zones);

        // Simulate a blitz cast: alt-cost OnResolved flips BlitzWasPaid before
        // the creature enters the battlefield.
        var (alt, _) = TenaciousUnderdogFactory.BuildBlitzCost();
        alt.OnResolved(c, _alice);
        c.BlitzWasPaid.Should().BeTrue();

        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        c.HasSummoningSickness = true;

        // Fire the ETB rider effect (the OnEnterBattlefieldSelf trigger).
        var etb = EtbRider(c);
        foreach (var e in etb.Effects) e.Execute();

        c.HasSummoningSickness.Should().BeFalse("CR 702.152b — blitz grants haste");

        // The delayed end-step sacrifice is now registered: firing the End step
        // queues it; resolve the stack to sacrifice the creature (CR 701.16 →
        // owner's graveyard).
        bus.Publish(new StepStartedEvent(StepStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        var resolver = new StackResolver(bus, zones);
        while (!stack.IsEmpty) resolver.ResolveTop(stack);

        c.Zone.Should().Be(ZoneType.Graveyard, "CR 702.152b — sacrifice at the next end step");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(c);
        _alice.Zones.Graveyard.GetCards().Should().Contain(c);
    }

    [Fact]
    public void EtbRider_NoBlitz_NoOp()
    {
        var c = TenaciousUnderdogFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        c.HasSummoningSickness = true;

        // BlitzWasPaid == false → ApplyEntersRiders no-ops.
        var etb = EtbRider(c);
        foreach (var e in etb.Effects) e.Execute();

        c.HasSummoningSickness.Should().BeTrue("no blitz → no haste grant (CR 702.152c)");
    }

    [Fact]
    public void DiesDrawRider_FiresOnDies_WhenBlitzPaid()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(), bus);

        var c = TenaciousUnderdogFactory.Create(_alice, triggers, zones);
        c.BlitzWasPaid = true;
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var libCard = new Creature("Filler", "{G}", 1, 1) { Owner = _alice };
        _alice.Zones.Library.AddCard(libCard);
        var handBefore = _alice.Zones.Hand.GetCards().Count();

        // Fire the dies-draw rider effect directly (intervening-if true).
        var dies = DiesTrigger(c);
        dies.InterveningIf!().Should().BeTrue();
        foreach (var e in dies.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1,
            "CR 702.152b — when a blitzed creature dies, draw a card");
    }

    private static TriggeredAbility DiesTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

    private static TriggeredAbility EtbRider(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf == null);
}
