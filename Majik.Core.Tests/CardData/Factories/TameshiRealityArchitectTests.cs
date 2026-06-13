using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tests.Helpers;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TameshiRealityArchitectFactory"/>
/// (Kamigawa: Neon Dynasty, {2}{U}). Legendary Creature — Moonfolk Wizard 2/3.
///
/// Covers BOTH abilities:
///   - the once-each-turn noncreature-bounce draw trigger ("Whenever one or
///     more noncreature permanents are returned to hand, draw a card. This
///     ability triggers only once each turn.");
///   - the "{X}{W}, Return a land you control to hand: Return target
///     artifact/enchantment card with mv ≤ X from your graveyard to the
///     battlefield. Activate only as a sorcery." reanimation, now FULL via the
///     GAP 2 per-activation X ledger (chosen X read off
///     <see cref="Abilities.ResolutionContext.ChosenX"/>).
/// </summary>
public class TameshiRealityArchitectTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void Seed(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Sorcery($"Lib {p.Name} {i}", "{1}");
            c.SetOwner(p);
            p.Zones.Library.AddCard(c);
        }
    }

    private TameshiState Build()
    {
        Seed(_alice, 5);
        var bus = new EventBus();
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(bus), bus);
        var tameshi = TameshiRealityArchitectFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(tameshi);
        tameshi.SetZone(ZoneType.Battlefield);
        return new TameshiState(tameshi, bus, triggers);
    }

    private sealed record TameshiState(Creature Card, EventBus Bus, TriggerManager Triggers);

    private static Artifact MakeArtifact(Player owner)
    {
        var a = new Artifact("Trinket", "{0}");
        a.SetOwner(owner);
        a.SetController(owner);
        return a;
    }

    private static Creature MakeBear(Player owner)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    [Fact]
    public void Tameshi_Identity()
    {
        var c = TameshiRealityArchitectFactory.Create(_alice);

        c.Name.Should().Be("Tameshi, Reality Architect");
        c.ManaCost.Should().Be("{2}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.Subtypes.Should().Contain(CardSubtype.Moonfolk);
        c.Subtypes.Should().Contain(CardSubtype.Wizard);
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the noncreature-bounce draw trigger is the one triggered ability");
    }

    [Fact]
    public void Tameshi_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Tameshi, Reality Architect", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Tameshi, Reality Architect");
    }

    [Fact]
    public void Tameshi_NoncreatureReturnedToHand_DrawsOne()
    {
        var s = Build();
        var startHand = _alice.Zones.Hand.GetCards().Count();

        var artifact = MakeArtifact(_alice);
        var ev = new CardMovedEvent(artifact, ZoneType.Battlefield, ZoneType.Hand);

        var trigger = s.Card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(ev));
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(startHand + 1,
            "a noncreature permanent returned to hand draws a card");
    }

    [Fact]
    public void Tameshi_TwoNoncreaturesSameEvent_StillDrawsOnlyOnce()
    {
        var s = Build();
        var startHand = _alice.Zones.Hand.GetCards().Count();

        // Two separate CardMovedEvents in the SAME turn ("one or more ...
        // returned to hand" + "triggers only once each turn").
        var a1 = MakeArtifact(_alice);
        var a2 = MakeArtifact(_alice);
        var ev1 = new CardMovedEvent(a1, ZoneType.Battlefield, ZoneType.Hand);
        var ev2 = new CardMovedEvent(a2, ZoneType.Battlefield, ZoneType.Hand);

        var trigger = s.Card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(ev1).Should().BeTrue("first noncreature bounce arms the trigger");
        foreach (var e in trigger.Effects) e.Execute();

        trigger.IsTriggered(ev2).Should().BeFalse(
            "the ability triggers only once each turn — the second bounce does not");

        _alice.Zones.Hand.GetCards().Should().HaveCount(startHand + 1,
            "only one draw across two noncreature bounces in the same turn");
    }

    [Fact]
    public void Tameshi_SecondBounceSameTurn_DoesNotFire()
    {
        var s = Build();

        var a1 = MakeArtifact(_alice);
        var ev1 = new CardMovedEvent(a1, ZoneType.Battlefield, ZoneType.Hand);
        var trigger = s.Card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(ev1).Should().BeTrue();

        var a2 = MakeArtifact(_alice);
        var ev2 = new CardMovedEvent(a2, ZoneType.Battlefield, ZoneType.Hand);
        trigger.IsTriggered(ev2).Should().BeFalse(
            "once-per-turn gate: a second bounce in the same turn does not trigger");
    }

    [Fact]
    public void Tameshi_CreatureReturnedToHand_DoesNotFire()
    {
        var s = Build();

        var bear = MakeBear(_alice);
        var ev = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Hand);

        var trigger = s.Card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(ev).Should().BeFalse(
            "a CREATURE returned to hand does not satisfy 'noncreature permanents'");
    }

    [Fact]
    public void Tameshi_NonHandDestination_DoesNotFire()
    {
        var s = Build();

        var artifact = MakeArtifact(_alice);
        var ev = new CardMovedEvent(artifact, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = s.Card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(ev).Should().BeFalse(
            "destroyed (→ graveyard) is not 'returned to hand'");
    }

    [Fact]
    public void Tameshi_TriggerReArmsNextTurn()
    {
        var s = Build();
        var startHand = _alice.Zones.Hand.GetCards().Count();

        var a1 = MakeArtifact(_alice);
        var trigger = s.Card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(new CardMovedEvent(a1, ZoneType.Battlefield, ZoneType.Hand))
            .Should().BeTrue();
        foreach (var e in trigger.Effects) e.Execute();

        // New turn — the once-per-turn gate resets on TurnStartedEvent.
        s.Bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        var a2 = MakeArtifact(_alice);
        trigger.IsTriggered(new CardMovedEvent(a2, ZoneType.Battlefield, ZoneType.Hand))
            .Should().BeTrue("the once-per-turn gate re-arms at the start of the next turn");
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(startHand + 2,
            "one draw per turn across two turns");
    }

    // -----------------------------------------------------------------------
    // GAP 2 — "{X}{W}, Return a land you control to its owner's hand: Return
    // target artifact or enchantment card with mana value X or less from your
    // graveyard to the battlefield. Activate only as a sorcery." (NOW emitted.)
    // -----------------------------------------------------------------------

    private static Land MakeLand(Player owner)
    {
        var l = NamedCardFactory.Create("Plains", owner);
        owner.Zones.Battlefield.AddCard(l);
        l.SetZone(ZoneType.Battlefield);
        return (Land)l;
    }

    private static Artifact MakeGyArtifact(Player owner, string cost)
    {
        var a = new Artifact("Relic", cost);
        a.SetOwner(owner);
        a.SetController(owner);
        owner.Zones.Graveyard.AddCard(a);
        a.SetZone(ZoneType.Graveyard);
        return a;
    }

    private static ActivatedAbility ReanimateAbility(Creature tameshi) =>
        tameshi.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(m => m.Cost.HasX));

    [Fact]
    public void Tameshi_HasReanimationAbility_XW_ReturnLand_SorcerySpeed()
    {
        var c = TameshiRealityArchitectFactory.Create(_alice);

        var reanimate = ReanimateAbility(c);
        reanimate.IsSorcerySpeed.Should().BeTrue("\"Activate only as a sorcery\" (CR 117.1a)");
        reanimate.Costs.OfType<ManaCostCost>().Single().Cost.HasX.Should().BeTrue(
            "the cost is {X}{W} — variable X");
        reanimate.Costs.OfType<ManaCostCost>().Single().Cost.White.Should().Be(1,
            "the cost has a {W} pip");
        reanimate.Costs.OfType<ReturnALandCost>().Should().ContainSingle(
            "the additional cost returns a land you control to hand");
        reanimate.TargetRequests.Should().HaveCount(1,
            "the ability targets one artifact/enchantment card in the graveyard");
    }

    [Fact]
    public void Tameshi_Reanimate_X3_ReturnsMv3Artifact_AndBounceTriggerFires()
    {
        ZoneServiceRegistry.Clear();
        try
        {
            Seed(_alice, 5);
            var bus = new EventBus();
            var zones = new ZoneService(bus);
            ZoneServiceRegistry.Set(_alice, zones);
            var triggers = new TriggerManager(new Majik.Core.Stack.Stack(bus), bus);

            var tameshi = TameshiRealityArchitectFactory.Create(_alice, bus, triggers);
            _alice.Zones.Battlefield.AddCard(tameshi);
            tameshi.SetZone(ZoneType.Battlefield);

            var land = MakeLand(_alice);
            var relic = MakeGyArtifact(_alice, "{3}"); // mv 3 — legal at X=3.

            var reanimate = ReanimateAbility(tameshi);

            // Pay the "Return a land you control to hand" additional cost. Routed
            // through ZoneService → CardMovedEvent fires the bounce-draw trigger.
            var landCost = reanimate.Costs.OfType<ReturnALandCost>().Single();
            landCost.Pay(_alice);

            land.Zone.Should().Be(ZoneType.Hand, "the land was returned to hand by the cost");

            // GAP 2 — chosen X + target threaded through resolution.
            reanimate.SetChosenX(3);
            reanimate.SetChosenTargets(new[] { new object[] { relic } });
            ContextResolve.Resolve(reanimate, _alice);

            relic.Zone.Should().Be(ZoneType.Battlefield,
                "X=3 ≥ the mv-3 artifact's cost → reanimated to the battlefield");
            _alice.Zones.Battlefield.GetCards().Should().Contain(relic);
        }
        finally
        {
            ZoneServiceRegistry.Clear();
        }
    }

    [Fact]
    public void Tameshi_Reanimate_Mv4_IsIllegal_AtX3()
    {
        ZoneServiceRegistry.Clear();
        try
        {
            var bus = new EventBus();
            var zones = new ZoneService(bus);
            ZoneServiceRegistry.Set(_alice, zones);

            var tameshi = TameshiRealityArchitectFactory.Create(_alice, bus, triggers: null);
            _alice.Zones.Battlefield.AddCard(tameshi);
            tameshi.SetZone(ZoneType.Battlefield);

            var bigRelic = MakeGyArtifact(_alice, "{4}"); // mv 4 — illegal at X=3.

            var reanimate = ReanimateAbility(tameshi);
            reanimate.SetChosenX(3);
            reanimate.SetChosenTargets(new[] { new object[] { bigRelic } });
            ContextResolve.Resolve(reanimate, _alice);

            bigRelic.Zone.Should().Be(ZoneType.Graveyard,
                "X=3 < the mv-4 artifact's cost → the reanimation fizzles (mv ≤ X gate)");
            _alice.Zones.Battlefield.GetCards().Should().NotContain(bigRelic);
        }
        finally
        {
            ZoneServiceRegistry.Clear();
        }
    }

    [Fact]
    public void Tameshi_Reanimate_BounceLandCost_FiresOncePerTurnDrawTrigger()
    {
        ZoneServiceRegistry.Clear();
        try
        {
            var bus = new EventBus();
            var zones = new ZoneService(bus);
            ZoneServiceRegistry.Set(_alice, zones);

            // No TriggerManager registration here so the once-per-turn gate is not
            // pre-consumed; we assert the trigger CONDITION against the bounce
            // event the cost publishes.
            var tameshi = TameshiRealityArchitectFactory.Create(_alice, bus, triggers: null);
            _alice.Zones.Battlefield.AddCard(tameshi);
            tameshi.SetZone(ZoneType.Battlefield);
            var land = MakeLand(_alice);

            var reanimate = ReanimateAbility(tameshi);
            var landCost = reanimate.Costs.OfType<ReturnALandCost>().Single();

            // The bounce publishes CardMovedEvent(land, Battlefield → Hand). The
            // once-per-turn bounce-draw trigger condition matches a noncreature
            // permanent returned to hand.
            CardMovedEvent? observed = null;
            bus.Subscribe<CardMovedEvent>(e => observed = e);
            landCost.Pay(_alice);

            observed.Should().NotBeNull("the land bounce publishes a CardMovedEvent");
            var trigger = tameshi.Abilities.OfType<TriggeredAbility>().Single();
            trigger.IsTriggered(observed!).Should().BeTrue(
                "returning a land (a noncreature permanent) to hand fires Tameshi's draw trigger");
        }
        finally
        {
            ZoneServiceRegistry.Clear();
        }
    }
}
