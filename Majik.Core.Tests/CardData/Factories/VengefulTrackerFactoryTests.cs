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
/// Unit tests for <see cref="VengefulTrackerFactory"/> (Murders at Karlov
/// Manor, {1}{R}).
///
/// Creature — Human Detective 2/2. Oracle text (Scryfall, verified):
///   "Whenever an opponent sacrifices an artifact, this creature deals 2
///    damage to them."
///
/// Vengeful Tracker is shipped as a DECLARATIVE card
/// (<c>CardData/Cards/vengeful-tracker.json</c>) — the first SHIPPED card to
/// consume the opponent-scoped <c>whenever_an_opponent_sacrifices_permanent</c>
/// trigger over <see cref="PermanentSacrificedEvent"/> paired with the untargeted
/// <c>deal_damage_to_triggering_player</c> payoff. These tests cover identity,
/// <see cref="NamedCardFactory"/> dispatch (the generated JSON-factory arm), and
/// the opponent-sacrifices-an-artifact trigger predicate: it fires only when an
/// OPPONENT sacrifices an ARTIFACT (not the controller's own sacrifice, not a
/// non-artifact), including a token artifact, and on resolution deals 2 damage to
/// that opponent.
/// </summary>
public class VengefulTrackerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void VengefulTracker_Identity()
    {
        var c = VengefulTrackerFactory.Create(_alice);

        c.Name.Should().Be("Vengeful Tracker");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Subtypes.Should().Contain(CardSubtype.Human);
        c.Subtypes.Should().Contain(CardSubtype.Detective);
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void VengefulTracker_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Vengeful Tracker", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Vengeful Tracker");
    }

    private static TriggeredAbility SacTrigger(Creature card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<PermanentSacrificedEvent>);

    [Fact]
    public void Trigger_FiresWhenOpponentSacrificesArtifact()
    {
        var vt = VengefulTrackerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vt);
        vt.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Bottle Cap", "{1}");
        artifact.SetOwner(_bob);
        artifact.SetController(_bob);

        var e = new PermanentSacrificedEvent(artifact, _bob, wasToken: false);

        SacTrigger(vt).IsTriggered(e).Should().BeTrue(
            "an opponent (Bob) sacrificed an artifact");
    }

    [Fact]
    public void Trigger_DoesNotFireOnOwnSacrifice()
    {
        var vt = VengefulTrackerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vt);
        vt.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Bottle Cap", "{1}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var e = new PermanentSacrificedEvent(artifact, _alice, wasToken: false);

        SacTrigger(vt).IsTriggered(e).Should().BeFalse(
            "Vengeful Tracker only triggers on an OPPONENT's sacrifice (CR 102.2)");
    }

    [Fact]
    public void Trigger_DoesNotFireOnNonArtifact()
    {
        var vt = VengefulTrackerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vt);
        vt.SetZone(ZoneType.Battlefield);

        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_bob);
        creature.SetController(_bob);

        var e = new PermanentSacrificedEvent(creature, _bob, wasToken: false);

        SacTrigger(vt).IsTriggered(e).Should().BeFalse(
            "the sacrificed permanent must be an artifact (CR 205.2)");
    }

    [Fact]
    public void Trigger_FiresOnTokenArtifactSacrifice()
    {
        // "an opponent sacrifices an artifact" fires on a token too — unlike
        // It That Betrays' "nontoken" clause, there is no token filter here.
        var vt = VengefulTrackerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vt);
        vt.SetZone(ZoneType.Battlefield);

        var treasure = new Artifact("Treasure", "{0}");
        treasure.SetOwner(_bob);
        treasure.SetController(_bob);

        var e = new PermanentSacrificedEvent(treasure, _bob, wasToken: true);

        SacTrigger(vt).IsTriggered(e).Should().BeTrue(
            "Vengeful Tracker has no nontoken restriction — a Treasure token counts");
    }

    [Fact]
    public void Trigger_StampsSacrificingOpponentAsThem()
    {
        // CR 603.3 — the trigger records the sacrificing opponent ("them") on the
        // ability as it matches, for the untargeted payoff to read at resolution.
        var vt = VengefulTrackerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vt);
        vt.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Bottle Cap", "{1}");
        artifact.SetOwner(_bob);
        artifact.SetController(_bob);

        var trigger = SacTrigger(vt);
        trigger.IsTriggered(new PermanentSacrificedEvent(artifact, _bob, wasToken: false))
            .Should().BeTrue();

        trigger.TriggeringPlayer.Should().BeSameAs(_bob,
            "the trigger stamps the sacrificing opponent as 'them' (CR 603.3)");
    }

    [Fact]
    public void Trigger_OnResolution_Deals2DamageToTheSacrificingOpponent()
    {
        // The "deals 2 damage to them" payoff is untargeted (reads the stamped
        // triggering player) so resolution must go through the stack, not a raw
        // Effect.Execute().
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var vt = VengefulTrackerFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(vt);
        vt.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Bottle Cap", "{1}");
        artifact.SetOwner(_bob);
        artifact.SetController(_bob);

        bus.Publish(new PermanentSacrificedEvent(artifact, _bob, wasToken: false));
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Pop() is { } top) top.Resolve();

        _bob.LifeTotal.Should().Be(18,
            "Vengeful Tracker deals 2 damage to the opponent who sacrificed the artifact (CR 603.3 'them')");
        _alice.LifeTotal.Should().Be(20, "only the sacrificing opponent takes damage");
    }

    [Fact]
    public void Trigger_RegistersWithTriggerManager_AndEnqueuesOnOpponentArtifactSacrifice()
    {
        var stack = new Majik.Core.Stack.Stack();
        var bus = new EventBus();
        var triggers = new TriggerManager(stack, bus);

        var vt = VengefulTrackerFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(vt);
        vt.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Bottle Cap", "{1}");
        artifact.SetOwner(_bob);
        artifact.SetController(_bob);

        triggers.EvaluateTriggers(
            new PermanentSacrificedEvent(artifact, _bob, wasToken: false));

        triggers.PendingCount.Should().Be(1,
            "the registered trigger enqueues when an opponent sacrifices an artifact");
    }
}
