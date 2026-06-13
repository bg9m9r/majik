using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TameshiRealityArchitectFactory"/>
/// (Kamigawa: Neon Dynasty, {2}{U}). Legendary Creature — Moonfolk Wizard 2/3.
///
/// Covers the implemented half — the once-each-turn noncreature-bounce draw
/// trigger:
///   "Whenever one or more noncreature permanents are returned to hand, draw a
///    card. This ability triggers only once each turn."
///
/// The {X}{W} reanimation activated ability is DEFERRED (engine has no
/// per-activation X ledger for activated abilities); see the factory xmldoc +
/// <see cref="KnownPartialImplementations"/>. These tests assert the trigger
/// only.
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
}
