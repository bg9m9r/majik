using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Tourach, Dread Cantor — Legendary Creature — Human
/// Cleric {1}{B}, 2/1.
///
/// Oracle text (Scryfall verified 2026-06):
///   "Kicker {B}{B}
///    Protection from white
///    Whenever an opponent discards a card, put a +1/+1 counter on Tourach.
///    When Tourach enters, if it was kicked, target opponent discards two
///    cards at random."
///
/// All four riders are built on existing engine primitives:
///   * Protection from white (CR 702.16) — ProtectionAbility marker.
///   * Kicker {B}{B} (CR 702.33) — KickerAdditionalCost + probe registry.
///   * Opponent-discard trigger (CR 603.1 / 701.16a) — CardMovedEvent
///     (Hand → Graveyard) gated to a non-controller discarder; +1/+1 counter
///     via CountersService.Add.
///   * Kicked-ETB trigger (CR 603.4 / 702.33b) — OnEnterBattlefieldSelf with
///     an intervening-if on Card.WasKicked; target opponent discards two at
///     random (CR 701.16e, seedable RNG).
/// </summary>
[Trait("Color", "B")]
public class TourachDreadCantorTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Tourach_IsLegendaryHumanClericCreature_AtCost1B_2_1()
    {
        var tourach = TourachDreadCantorFactory.Create(_alice);

        tourach.Name.Should().Be("Tourach, Dread Cantor");
        tourach.HasType(CardType.Creature).Should().BeTrue();
        tourach.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        tourach.HasSubtype(CardSubtype.Human).Should().BeTrue();
        tourach.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        tourach.ManaCost.Should().Be("{1}{B}");
        tourach.Power.Should().Be(2);
        tourach.Toughness.Should().Be(1);
        tourach.Owner.Should().BeSameAs(_alice);
        tourach.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Protection from white — CR 702.16
    // -----------------------------------------------------------------------

    [Fact]
    public void Tourach_HasProtectionFromWhite()
    {
        var tourach = TourachDreadCantorFactory.Create(_alice);

        tourach.Abilities
            .OfType<ProtectionAbility>()
            .Should().ContainSingle(p => p.Quality == "white");
    }

    // -----------------------------------------------------------------------
    // Kicker {B}{B} — CR 702.33
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildAdditionalCost_ReturnsKickerCostAtBB()
    {
        var tourach = TourachDreadCantorFactory.Create(_alice);
        var cost = TourachDreadCantorFactory.BuildAdditionalCost(tourach);

        cost.Should().BeOfType<KickerAdditionalCost>();
        ((KickerAdditionalCost)cost).KickerCost.Should().Be(ManaCost.Parse("{B}{B}"));
    }

    [Fact]
    public void KickerAltCostProbe_Recognises_Tourach()
    {
        var tourach = TourachDreadCantorFactory.Create(_alice);
        tourach.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(tourach);

        var probe = new KickerAltCostProbe();
        probe.KickerCostFor(tourach, _alice).Should().Be(ManaCost.Parse("{B}{B}"));
    }

    // -----------------------------------------------------------------------
    // Opponent-discard trigger — "Whenever an opponent discards a card, put a
    // +1/+1 counter on Tourach." CR 603.1 / 701.16a / 122.1.
    // -----------------------------------------------------------------------

    [Fact]
    public void DiscardTrigger_IsBattlefieldActive()
    {
        var tourach = TourachDreadCantorFactory.Create(_alice);

        var discardTrigger = tourach.Abilities
            .OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf == null
                && t.ActiveZones.Contains(ZoneType.Battlefield));

        discardTrigger.Should().NotBeNull();
    }

    [Fact]
    public void DiscardTrigger_Fires_WhenOpponentDiscards_PutsPlusOneCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var tourach = TourachDreadCantorFactory.Create(_alice, triggers);
        tourach.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(tourach);

        // Bob (an opponent) discards a card: Hand → Graveyard.
        var bobCard = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobCard.SetOwner(_bob);
        bus.Publish(new CardMovedEvent(bobCard, ZoneType.Hand, ZoneType.Graveyard));

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "an opponent discarding a card triggers Tourach");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        tourach.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "one +1/+1 counter per opponent discard (CR 122.1)");
    }

    [Fact]
    public void DiscardTrigger_DoesNotFire_WhenControllerDiscards()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var tourach = TourachDreadCantorFactory.Create(_alice, triggers);
        tourach.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(tourach);

        // Alice (Tourach's controller) discards — "an opponent" excludes you.
        var aliceCard = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceCard.SetOwner(_alice);
        bus.Publish(new CardMovedEvent(aliceCard, ZoneType.Hand, ZoneType.Graveyard));

        triggers.PendingCount.Should().Be(0,
            "your own discard is not 'an opponent discards' (CR 102.1)");
    }

    // -----------------------------------------------------------------------
    // Kicked-ETB trigger — "When Tourach enters, if it was kicked, target
    // opponent discards two cards at random." CR 603.4 / 702.33b / 701.16e.
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_HasInterveningIf_AndTargetRequest()
    {
        var tourach = TourachDreadCantorFactory.Create(_alice);

        var etbTrigger = tourach.Abilities
            .OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

        etbTrigger.TargetRequests.Should().ContainSingle();
        etbTrigger.TargetRequests[0].Description.Should().Be("target opponent");
    }

    [Fact]
    public void EtbTrigger_NotKicked_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var tourach = TourachDreadCantorFactory.Create(_alice, triggers);
        // WasKicked stays false.
        tourach.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(tourach, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(0,
            "not kicked → intervening-if false → ETB trigger never queues (CR 603.4)");
    }

    [Fact]
    public void EtbTrigger_Kicked_TargetOpponentDiscardsTwoAtRandom()
    {
        GameRandomRegistry.Set(_bob, new GameRandom(seed: 12345));
        try
        {
            var bus = new EventBus();
            var stack = new Majik.Core.Stack.Stack(bus);
            var triggers = new TriggerManager(stack, bus);

            var tourach = TourachDreadCantorFactory.Create(_alice, triggers);
            tourach.SetWasKicked(true);
            tourach.SetZone(ZoneType.Battlefield);

            // Bob holds four cards.
            for (var i = 0; i < 4; i++)
            {
                var c = new Creature($"Bear {i}", "{1}{G}", 2, 2);
                c.SetOwner(_bob);
                c.SetZone(ZoneType.Hand);
                _bob.Zones.Hand.AddCard(c);
            }

            bus.Publish(new CardMovedEvent(tourach, ZoneType.Hand, ZoneType.Battlefield));

            triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
                "kicked → intervening-if true → ETB trigger queues");

            // Set the chosen target opponent (caster's choice — CR 601.2c
            // analogue for triggered targets) before resolution.
            var etbTrigger = tourach.Abilities
                .OfType<TriggeredAbility>()
                .Single(t => t.InterveningIf != null);
            etbTrigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();

            _bob.Zones.Hand.GetCards().Should().HaveCount(2,
                "Bob started with 4 and discards two cards at random");
            _bob.Zones.Graveyard.GetCards().Should().HaveCount(2);
        }
        finally
        {
            GameRandomRegistry.Remove(_bob);
        }
    }

    [Fact]
    public void EtbTrigger_Kicked_FewerThanTwoCards_DiscardsWhatIsThere()
    {
        GameRandomRegistry.Set(_bob, new GameRandom(seed: 7));
        try
        {
            var bus = new EventBus();
            var stack = new Majik.Core.Stack.Stack(bus);
            var triggers = new TriggerManager(stack, bus);

            var tourach = TourachDreadCantorFactory.Create(_alice, triggers);
            tourach.SetWasKicked(true);
            tourach.SetZone(ZoneType.Battlefield);

            // Bob holds a single card.
            var only = new Creature("Lone Bear", "{1}{G}", 2, 2);
            only.SetOwner(_bob);
            only.SetZone(ZoneType.Hand);
            _bob.Zones.Hand.AddCard(only);

            bus.Publish(new CardMovedEvent(tourach, ZoneType.Hand, ZoneType.Battlefield));

            var etbTrigger = tourach.Abilities
                .OfType<TriggeredAbility>()
                .Single(t => t.InterveningIf != null);
            etbTrigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();

            _bob.Zones.Hand.GetCards().Should().BeEmpty(
                "fewer than two cards → discard what is there (CR 701.16a)");
            _bob.Zones.Graveyard.GetCards().Should().HaveCount(1);
        }
        finally
        {
            GameRandomRegistry.Remove(_bob);
        }
    }
}
