using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Vengeful Tracker (Murders at Karlov Manor, {1}{R}) — shipped as a
/// DECLARATIVE fileless card (<c>CardData/Cards/vengeful-tracker.json</c>), the
/// canonical demonstration of the new
/// <c>whenever_an_opponent_sacrifices_permanent</c> trigger (the opponent-scoped
/// aristocrat payoff-consumer over <see cref="PermanentSacrificedEvent"/>;
/// v1-deferral "opponent-sacrifices-aristocrat-payoff-trigger") paired with the
/// untargeted <c>deal_damage_to_triggering_player</c> verb.
///
/// Oracle (Scryfall, verified): "Whenever an opponent sacrifices an artifact,
/// this creature deals 2 damage to them."
///
/// Covers:
/// - Identity (Human Detective 2/2, mana cost {1}{R}).
/// - NamedCardFactory dispatch (the generated fileless-JSON arm).
/// - Trigger fires (and deals 2 to the opponent) when an opponent sacrifices an
///   artifact (CR 701.16 / CR 603.3 "them").
/// - Trigger does NOT fire when the CONTROLLER sacrifices an artifact (CR 102.2).
/// - Trigger does NOT fire when an opponent sacrifices a non-artifact (CR 205.2).
/// </summary>
public class VengefulTrackerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature Build(Player owner) =>
        (Creature)NamedCardFactory.Create("Vengeful Tracker", owner);

    private Creature BuildAndRegister(Player owner, TriggerManager triggers)
    {
        var card = Build(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        var ability = card.Abilities.OfType<TriggeredAbility>().Single();
        triggers.RegisterTriggeredAbility(ability);
        return card;
    }

    private static Artifact OpponentArtifact(Player owner)
    {
        var a = new Artifact("Sacrificed Trinket", "{1}");
        a.SetOwner(owner);
        a.SetController(owner);
        return a;
    }

    private static Creature OpponentCreature(Player owner)
    {
        var c = new Creature("Sacrificed Goblin", "{R}", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private static void ResolveAll(Majik.Core.Stack.Stack stack)
    {
        while (true)
        {
            var top = stack.Pop();
            if (top == null) break;
            top.Resolve();
        }
    }

    [Fact]
    public void Identity_HumanDetective_TwoTwo_OneRed()
    {
        var card = Build(_alice);

        card.Name.Should().Be("Vengeful Tracker");
        card.Power.Should().Be(2);
        card.Toughness.Should().Be(2);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Detective).Should().BeTrue();
        card.ManaCostValue.TotalValue.Should().Be(2);
    }

    [Fact]
    public void OpponentSacrificesArtifact_Deals2ToThem()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(_alice, triggers);

        bus.Publish(new PermanentSacrificedEvent(OpponentArtifact(_bob), _bob, wasToken: false));

        triggers.PendingCount.Should().Be(1, "an opponent's artifact sacrifice fires Vengeful Tracker (CR 701.16)");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        _bob.LifeTotal.Should().Be(18, "Vengeful Tracker deals 2 damage to the sacrificing opponent (CR 603.3 'them')");
        _bob.LifeLostThisTurn.Should().Be(2);
    }

    [Fact]
    public void ControllerSacrificesArtifact_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(_alice, triggers);

        bus.Publish(new PermanentSacrificedEvent(OpponentArtifact(_alice), _alice, wasToken: false));

        triggers.PendingCount.Should().Be(0, "the controller is never their own opponent (CR 102.2)");
        _alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void OpponentSacrificesNonArtifact_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(_alice, triggers);

        bus.Publish(new PermanentSacrificedEvent(OpponentCreature(_bob), _bob, wasToken: false));

        triggers.PendingCount.Should().Be(0, "only an artifact sacrifice fires it (CR 205.2)");
        _bob.LifeTotal.Should().Be(20);
    }
}
