using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for the v2 <see cref="OrcishBowmastersFactory"/> — Creature —
/// Orc Archer {1}{B} 1/1 (The Lord of the Rings) with Flash + the
/// combined ETB / opponent-draw trigger and the printed Amass Orcs 1
/// rider.
///
/// Coverage:
///   - Card identity (Creature, {1}{B}, 1/1, Orc + Archer subtypes,
///     owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Flash keyword marker.
///   - Two TriggeredAbilities — one ETB (CardMovedEvent), one
///     opponent-draw (CardDrawnEvent). Both restricted to Battlefield.
///   - ETB resolve: 1 damage to controller (deterministic fallback) +
///     Amass Orcs 1 creates a 1/1 Orc Army with one +1/+1 counter
///     (token spec 0/0 + 1 counter = 1/1).
///   - Bus integration: opponent's first draw of their draw step does
///     NOT fire; second draw DOES.
///   - Bus integration: controller's own draws never fire.
///   - Bus integration: opponent draw outside their draw step still
///     fires (the "first one they draw in each of their draw steps"
///     exception is narrow).
/// </summary>
[Trait("Color", "B")]
public class OrcishBowmastersFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void OrcishBowmasters_Identity_Creature_OrcArcher_1_1_At1B()
    {
        var bow = OrcishBowmastersFactory.Create(_alice);

        bow.Should().BeOfType<Creature>();
        bow.Name.Should().Be("Orcish Bowmasters");
        bow.ManaCost.Should().Be("{1}{B}");
        bow.HasType(CardType.Creature).Should().BeTrue();
        bow.HasSubtype(CardSubtype.Orc).Should().BeTrue();
        bow.HasSubtype(CardSubtype.Archer).Should().BeTrue();
        bow.BasePower.Should().Be(1);
        bow.BaseToughness.Should().Be(1);
        bow.Owner.Should().BeSameAs(_alice);
        bow.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void OrcishBowmasters_HasFlash_PlusTwoTriggers()
    {
        var bow = OrcishBowmastersFactory.Create(_alice);

        bow.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flash");

        var triggers = bow.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2,
            "ETB + opponent-draw are wired as two sibling triggers (per-event-type contract)");
        triggers.Should().AllSatisfy(t =>
            t.ActiveZones.Should().Contain(ZoneType.Battlefield));

        // Each trigger declares the "any target" 1..1 request.
        triggers.Should().AllSatisfy(t =>
        {
            t.TargetRequests.Should().ContainSingle();
            t.TargetRequests[0].MinTargets.Should().Be(1);
            t.TargetRequests[0].MaxTargets.Should().Be(1);
            t.TargetRequests[0].Description.Should().Be("any target");
        });
    }

    // -----------------------------------------------------------------------
    // ETB resolve — direct effect invocation (no agent target)
    // -----------------------------------------------------------------------

    [Fact]
    public void ETB_ResolveWithChosenOpponentTarget_DealsDamage_AndAmassesOrcs()
    {
        // Stage Bowmasters on Alice's battlefield, choose Bob as the
        // damage target, run the ETB resolve directly.
        var bow = OrcishBowmastersFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bow);
        bow.SetZone(ZoneType.Battlefield);

        // The ETB trigger is the one whose condition is a CardMovedEvent.
        var etb = bow.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

        var bobLifeBefore = _bob.LifeTotal;
        foreach (var effect in etb.Effects) effect.Execute();

        _bob.LifeTotal.Should().Be(bobLifeBefore - 1, "Bowmasters deals 1 damage to Bob");

        // CR 701.49 — Amass Orcs 1. Since Alice controlled no Army,
        // a 0/0 black Orc Army token is created and gets one +1/+1
        // counter (effective 1/1).
        var army = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.HasSubtype(CardSubtype.Army));
        army.HasSubtype(CardSubtype.Orc).Should().BeTrue();
        army.BasePower.Should().Be(0, "Amass tokens enter at base 0/0");
        army.BaseToughness.Should().Be(0);
        army.Counters.Count(Majik.Core.Counters.CounterType.PlusOnePlusOne).Should().Be(1,
            "Amass Orcs 1 adds one +1/+1 counter (CR 701.49c)");
    }

    [Fact]
    public void ETB_ResolveWithChosenCreatureTarget_DealsDamageToCreature()
    {
        var bow = OrcishBowmastersFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bow);
        bow.SetZone(ZoneType.Battlefield);

        // Big 0/4 wall so the damage marker shows up without SBA wipe.
        var wall = new Creature("Wall of Wood", "{G}", 0, 4);
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(wall);
        wall.SetZone(ZoneType.Battlefield);

        var etb = bow.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { wall } });

        foreach (var effect in etb.Effects) effect.Execute();

        wall.Damage.Should().Be(1, "Bowmasters' ETB deals 1 damage to target creature");
    }

    // -----------------------------------------------------------------------
    // Live bus — opponent draw integration
    // -----------------------------------------------------------------------

    [Fact]
    public void DrawTrigger_LiveBus_OpponentFirstDrawOfDrawStep_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bow = OrcishBowmastersFactory.Create(_alice, bus, triggers, zones: null);
        _alice.Zones.Battlefield.AddCard(bow);
        bow.SetZone(ZoneType.Battlefield);

        // Bob's draw step begins → reset Bob's counter to 0.
        bus.Publish(new StepStartedEvent(PhaseStateType.Draw, _bob));

        // Bob draws the free draw — should NOT fire.
        bus.Publish(new CardDrawnEvent(new Card("Filler", "{0}"), _bob));

        triggers.PendingCount.Should().Be(0,
            "the first draw each draw step is the printed exception");
    }

    [Fact]
    public void DrawTrigger_LiveBus_OpponentSecondDrawOfDrawStep_Fires()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bow = OrcishBowmastersFactory.Create(_alice, bus, triggers, zones: null);
        _alice.Zones.Battlefield.AddCard(bow);
        bow.SetZone(ZoneType.Battlefield);

        // Bob's draw step begins → reset.
        bus.Publish(new StepStartedEvent(PhaseStateType.Draw, _bob));

        // First draw — free.
        bus.Publish(new CardDrawnEvent(new Card("Free", "{0}"), _bob));
        triggers.PendingCount.Should().Be(0);

        // Second draw (e.g. Howling Mine, Phyrexian Arena rider, etc.)
        // — fires Bowmasters.
        bus.Publish(new CardDrawnEvent(new Card("Extra", "{0}"), _bob));
        triggers.PendingCount.Should().Be(1,
            "the second draw in Bob's draw step is the first 'real' Bowmasters trigger");
    }

    [Fact]
    public void DrawTrigger_LiveBus_ControllersOwnDraw_NeverFires()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bow = OrcishBowmastersFactory.Create(_alice, bus, triggers, zones: null);
        _alice.Zones.Battlefield.AddCard(bow);
        bow.SetZone(ZoneType.Battlefield);

        // Alice's draw step + draws → Bowmasters' controller's own
        // draws never trigger (printed "an opponent draws").
        bus.Publish(new StepStartedEvent(PhaseStateType.Draw, _alice));
        bus.Publish(new CardDrawnEvent(new Card("AliceDraw1", "{0}"), _alice));
        bus.Publish(new CardDrawnEvent(new Card("AliceDraw2", "{0}"), _alice));

        triggers.PendingCount.Should().Be(0);
    }

    [Fact]
    public void DrawTrigger_LiveBus_OpponentOutOfStepDraw_Fires()
    {
        // Outside Bob's draw step (e.g. opponent cast a cantrip on
        // Alice's turn). The "first one they draw in each of their
        // draw steps" exception is narrow; out-of-step draws always
        // fire Bowmasters.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bow = OrcishBowmastersFactory.Create(_alice, bus, triggers, zones: null);
        _alice.Zones.Battlefield.AddCard(bow);
        bow.SetZone(ZoneType.Battlefield);

        // Bob's draw step happens, free draw consumes the counter slot.
        bus.Publish(new StepStartedEvent(PhaseStateType.Draw, _bob));
        bus.Publish(new CardDrawnEvent(new Card("StepDraw", "{0}"), _bob));
        triggers.PendingCount.Should().Be(0);

        // Now Alice's main phase — Bob casts Brainstorm and draws three.
        // Counter is at 1 from the step draw; each new draw bumps to
        // 2, 3, 4 — all fire.
        bus.Publish(new CardDrawnEvent(new Card("Brainstorm1", "{0}"), _bob));
        bus.Publish(new CardDrawnEvent(new Card("Brainstorm2", "{0}"), _bob));
        bus.Publish(new CardDrawnEvent(new Card("Brainstorm3", "{0}"), _bob));

        triggers.PendingCount.Should().Be(3,
            "every out-of-draw-step draw fires Bowmasters once");
    }

    [Fact]
    public void DrawTrigger_LiveBus_NewDrawStep_ResetsCounter_FreeDrawAgain()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bow = OrcishBowmastersFactory.Create(_alice, bus, triggers, zones: null);
        _alice.Zones.Battlefield.AddCard(bow);
        bow.SetZone(ZoneType.Battlefield);

        // Bob's draw step 1.
        bus.Publish(new StepStartedEvent(PhaseStateType.Draw, _bob));
        bus.Publish(new CardDrawnEvent(new Card("Turn1Free", "{0}"), _bob));
        triggers.PendingCount.Should().Be(0);

        // Bob's draw step 2 (next turn) — counter resets, free draw
        // again.
        bus.Publish(new StepStartedEvent(PhaseStateType.Draw, _bob));
        bus.Publish(new CardDrawnEvent(new Card("Turn2Free", "{0}"), _bob));
        triggers.PendingCount.Should().Be(0,
            "draw-step counter resets each draw step (CR 504.1)");
    }
}
