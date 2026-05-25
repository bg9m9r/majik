using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Slogurk, the Overslime (Innistrad: Crimson Vow, {1}{G}{U},
/// Legendary Creature — Ooze 3/3).
///
/// Covers:
/// - Identity (name, type, cost, P/T, Legendary supertype, Ooze subtype,
///   Trample keyword).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Counter-on-land-to-graveyard trigger: lands milled / sacrificed
///   from anywhere → +1/+1 counter on Slogurk.
/// - Filter: other players' lands hitting their graveyards do NOT
///   trigger Slogurk.
/// - Activated ability: with ≥3 counters, remove three, bounce to hand;
///   with &lt;3 counters, no-op.
/// - LTB trigger: when Slogurk leaves the battlefield, returns up to
///   three lands from controller's graveyard to hand.
/// </summary>
public class SlogurkTheOverslimeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public SlogurkTheOverslimeTests()
    {
        _zones = new ZoneService(_bus);
    }

    [Fact]
    public void Identity_NameTypeCostBody()
    {
        var card = SlogurkTheOverslimeFactory.Create(_alice);

        card.Name.Should().Be("Slogurk, the Overslime");
        card.ManaCost.Should().Be("{1}{G}{U}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Ooze).Should().BeTrue();

        var creature = card.Should().BeOfType<Creature>().Subject;
        creature.BasePower.Should().Be(3);
        creature.BaseToughness.Should().Be(3);
        creature.Owner.Should().BeSameAs(_alice);
        creature.Controller.Should().BeSameAs(_alice);

        CombatAbilities.HasTrample(creature).Should().BeTrue(
            "Slogurk prints Trample (CR 702.19)");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Slogurk()
    {
        var card = NamedCardFactory.Create("Slogurk, the Overslime", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Slogurk, the Overslime");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    /// <summary>
    /// Hand-resolve the counter-trigger effect (no TriggerManager): a
    /// CardMovedEvent for a land into Alice's graveyard increments
    /// Slogurk's +1/+1 counter by 1. We exercise the effect closure
    /// directly the same way other counter-trigger tests do.
    /// </summary>
    [Fact]
    public void CounterTrigger_OnLandToGraveyard_PlacesPlusOneCounter()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var card = SlogurkTheOverslimeFactory.Create(_alice, _zones, triggers);
        card.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(card);

        // A Forest hit the graveyard via mill / sac.
        var forest = NamedCardFactory.Create("Forest", _alice);
        // Move from Library → Graveyard via ZoneService so the event
        // publishes and the trigger registers.
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);
        _zones.MoveCard(forest, ZoneType.Library, ZoneType.Graveyard, _alice);

        // Land lives in graveyard now.
        forest.Zone.Should().Be(ZoneType.Graveyard);

        // Trigger should be pending — pull it onto the stack and resolve.
        triggers.PutPendingTriggersOnStack(_alice);
        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Slogurk's printed counter trigger fires when a land hits its "
            + "controller's graveyard (CR 603.1)");
    }

    [Fact]
    public void CounterTrigger_IgnoresOpponentLandToGraveyard()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var card = SlogurkTheOverslimeFactory.Create(_alice, _zones, triggers);
        card.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(card);

        // Bob's land into Bob's graveyard — Alice's Slogurk does NOT fire.
        var bobsForest = NamedCardFactory.Create("Forest", _bob);
        _bob.Zones.Library.AddCard(bobsForest);
        bobsForest.SetZone(ZoneType.Library);
        _zones.MoveCard(bobsForest, ZoneType.Library, ZoneType.Graveyard, _bob);

        triggers.PutPendingTriggersOnStack(_alice);
        // Stack should be empty — predicate filtered the event out.
        stack.IsEmpty.Should().BeTrue(
            "another player's land does not trigger Slogurk — printed text "
            + "reads 'your graveyard'");
        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void ActivatedAbility_WithThreeCounters_BouncesToHand()
    {
        var card = SlogurkTheOverslimeFactory.Create(_alice, _zones, triggers: null);
        card.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(card);
        card.Counters.Add(CounterType.PlusOnePlusOne, 3);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "three +1/+1 counters removed as part of the activation cost");
        card.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(card);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(card);
    }

    [Fact]
    public void ActivatedAbility_WithFewerCounters_IsNoOp()
    {
        var card = SlogurkTheOverslimeFactory.Create(_alice, _zones, triggers: null);
        card.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(card);
        card.Counters.Add(CounterType.PlusOnePlusOne, 2);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "insufficient counters → cost cannot be paid → activation no-ops");
        card.Zone.Should().Be(ZoneType.Battlefield);
    }

    /// <summary>
    /// LTB resolution behavior — invokes the LTB trigger's effect
    /// directly (mirrors <c>ThoughtKnotSeerFactoryTests.ThoughtKnotSeer_
    /// LtbEffect_TargetOpponentDrawsACard</c>'s pattern). End-to-end
    /// bus wiring of LTB triggers depends on TriggerManager observing
    /// the source's last-known battlefield zone before
    /// SyncCardRegistration deregisters it; that pipeline gap is
    /// shared by every battlefield-LTB factory in the repo today and
    /// is captured in CR 603.6d / 603.10c follow-up work.
    /// </summary>
    [Fact]
    public void LtbEffect_ReturnsUpToThreeLandsFromGraveyardToHand()
    {
        var card = SlogurkTheOverslimeFactory.Create(_alice, _zones, triggers: null);
        card.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(card);

        // Four lands in Alice's graveyard — LTB returns at most three.
        var lands = new[]
        {
            NamedCardFactory.Create("Forest", _alice),
            NamedCardFactory.Create("Mountain", _alice),
            NamedCardFactory.Create("Island", _alice),
            NamedCardFactory.Create("Plains", _alice),
        };
        foreach (var land in lands)
        {
            _alice.Zones.Graveyard.AddCard(land);
            land.SetZone(ZoneType.Graveyard);
        }

        var ltb = GetLtbTrigger(card);
        foreach (var effect in ltb.Effects) effect.Execute();

        var landsInHand = _alice.Zones.Hand.GetCards()
            .Where(c => c.HasType(CardType.Land))
            .ToList();
        landsInHand.Should().HaveCount(3,
            "LTB returns up to three target land cards from graveyard to hand");
        _alice.Zones.Graveyard.GetCards()
            .Count(c => c.HasType(CardType.Land))
            .Should().Be(1, "the fourth land stays in the graveyard");
    }

    [Fact]
    public void LtbCondition_MatchesOnSelfLeavesBattlefield()
    {
        var card = SlogurkTheOverslimeFactory.Create(_alice);
        var ltb = GetLtbTrigger(card);

        // CardMovedEvent for Slogurk leaving the battlefield (any
        // destination — graveyard / hand / exile / library).
        var toHand = new CardMovedEvent(
            card, ZoneType.Battlefield, ZoneType.Hand);
        ltb.Condition.Matches(toHand, ltb).Should().BeTrue();

        var toGraveyard = new CardMovedEvent(
            card, ZoneType.Battlefield, ZoneType.Graveyard);
        ltb.Condition.Matches(toGraveyard, ltb).Should().BeTrue();

        // A different card leaving the battlefield doesn't fire Slogurk's LTB.
        var ravager = new Creature("Arcbound Ravager", "{2}", 0, 0);
        var other = new CardMovedEvent(
            ravager, ZoneType.Battlefield, ZoneType.Graveyard);
        ltb.Condition.Matches(other, ltb).Should().BeFalse();
    }

    private static TriggeredAbility GetLtbTrigger(ICard card)
    {
        var probe = new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Hand);
        return card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition.Matches(probe, t));
    }
}
