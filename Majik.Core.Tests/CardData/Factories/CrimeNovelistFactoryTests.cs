using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CrimeNovelistFactory"/> (Outlaws of Thunder
/// Junction, {2}{R}).
///
/// Creature — Goblin Bard 1/3. Oracle text (Scryfall, verified):
///   "Whenever you sacrifice an artifact, put a +1/+1 counter on this creature
///    and add {R}."
///
/// Crime Novelist is a HAND-ROLLED factory (not a pure declarative JSON card)
/// because its triggered ability pairs TWO payoffs — a +1/+1 counter on itself
/// AND adding {R} to its controller's mana pool — and the declarative triggered
/// effect surface has no "add mana" effect kind (only the spell-resolution
/// <c>AddMana</c> body step exists). So the factory mirrors
/// <see cref="BirgiGodOfStorytellingFactory"/>'s trigger-adds-{R} pattern, scoped
/// to the controller-side <see cref="PermanentSacrificedEvent"/> (the
/// "you sacrifice …" predicate, CR 109.5 / CR 701.16).
///
/// These tests cover identity plus the unique trigger behaviour: it fires only
/// when the CONTROLLER sacrifices an ARTIFACT (not an opponent's sacrifice, not a
/// non-artifact), fires on a token artifact too, and on resolution places one
/// +1/+1 counter on Crime Novelist and adds {R} to the controller's pool.
/// </summary>
[Trait("Color", "R")]
public class CrimeNovelistFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void CrimeNovelist_Identity()
    {
        var c = CrimeNovelistFactory.Create(_alice);

        c.Name.Should().Be("Crime Novelist");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Subtypes.Should().Contain(CardSubtype.Goblin);
        c.Subtypes.Should().Contain(CardSubtype.Bard);
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    private static TriggeredAbility SacTrigger(Creature card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<PermanentSacrificedEvent>);

    [Fact]
    public void Trigger_FiresWhenYouSacrificeArtifact()
    {
        var cn = CrimeNovelistFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(cn);
        cn.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Bottle Cap", "{1}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var e = new PermanentSacrificedEvent(artifact, _alice, wasToken: false);

        SacTrigger(cn).IsTriggered(e).Should().BeTrue(
            "the controller (Alice) sacrificed an artifact (CR 109.5)");
    }

    [Fact]
    public void Trigger_DoesNotFireOnOpponentSacrifice()
    {
        var cn = CrimeNovelistFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(cn);
        cn.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Bottle Cap", "{1}");
        artifact.SetOwner(_bob);
        artifact.SetController(_bob);

        var e = new PermanentSacrificedEvent(artifact, _bob, wasToken: false);

        SacTrigger(cn).IsTriggered(e).Should().BeFalse(
            "Crime Novelist triggers only on YOUR sacrifice, not an opponent's (CR 109.5)");
    }

    [Fact]
    public void Trigger_DoesNotFireOnNonArtifact()
    {
        var cn = CrimeNovelistFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(cn);
        cn.SetZone(ZoneType.Battlefield);

        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var e = new PermanentSacrificedEvent(creature, _alice, wasToken: false);

        SacTrigger(cn).IsTriggered(e).Should().BeFalse(
            "the sacrificed permanent must be an artifact (CR 205.2)");
    }

    [Fact]
    public void Trigger_FiresOnTokenArtifactSacrifice()
    {
        // "Whenever you sacrifice an artifact" fires on a token artifact too —
        // there is no nontoken restriction (a Treasure/Clue counts).
        var cn = CrimeNovelistFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(cn);
        cn.SetZone(ZoneType.Battlefield);

        var treasure = new Artifact("Treasure", "{0}");
        treasure.SetOwner(_alice);
        treasure.SetController(_alice);

        var e = new PermanentSacrificedEvent(treasure, _alice, wasToken: true);

        SacTrigger(cn).IsTriggered(e).Should().BeTrue(
            "Crime Novelist has no nontoken restriction — a Treasure token counts");
    }

    [Fact]
    public void Trigger_OnResolution_PutsCounterAndAddsRed()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var cn = CrimeNovelistFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(cn);
        cn.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Bottle Cap", "{1}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        bus.Publish(new PermanentSacrificedEvent(artifact, _alice, wasToken: false));
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Pop() is { } top) top.Resolve();

        cn.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Crime Novelist puts a +1/+1 counter on itself when you sacrifice an artifact");
        _alice.ManaPool.Red.Should().Be(1,
            "the trigger adds {R} to the controller's mana pool");
    }

    [Fact]
    public void Trigger_RegistersWithTriggerManager_AndEnqueuesOnYourArtifactSacrifice()
    {
        var stack = new Majik.Core.Stack.Stack();
        var bus = new EventBus();
        var triggers = new TriggerManager(stack, bus);

        var cn = CrimeNovelistFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(cn);
        cn.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Bottle Cap", "{1}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        triggers.EvaluateTriggers(
            new PermanentSacrificedEvent(artifact, _alice, wasToken: false));

        triggers.PendingCount.Should().Be(1,
            "the registered trigger enqueues when the controller sacrifices an artifact");
    }
}
