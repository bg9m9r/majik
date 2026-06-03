using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Vindictive Vampire (Eldritch Moon, {3}{B}) — a pure-JSON card
/// consuming the declarative <c>whenever_another_creature_dies</c> trigger with
/// <c>youControlOnly</c>, paired with the <c>deal_damage_each_opponent</c> +
/// <c>gain_life_self</c> effect verbs.
///
///   "Whenever another creature you control dies, this creature deals 1 damage
///    to each opponent and you gain 1 life."
/// </summary>
[Trait("Color", "B")]
public class VindictiveVampireFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void VindictiveVampire_Identity()
    {
        var c = VindictiveVampireFactory.Create(_alice);

        c.Name.Should().Be("Vindictive Vampire");
        c.ManaCost.Should().Be("{3}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.GetPower().Should().Be(2);
        c.GetToughness().Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().ContainSingle();
        c.Abilities.OfType<TriggeredAbility>().Single()
            .TargetRequests.Should().BeEmpty(
                "deal_damage_each_opponent is untargeted (CR 608.2)");
    }

    [Fact]
    public void VindictiveVampire_AnotherCreatureYouControlDies_TriggerMatches()
    {
        var vamp = VindictiveVampireFactory.Create(_alice);
        vamp.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var trigger = vamp.Abilities.OfType<TriggeredAbility>().Single();
        var dies = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(dies, trigger).Should().BeTrue();
    }

    [Fact]
    public void VindictiveVampire_OpponentCreatureDies_DoesNotTrigger()
    {
        var vamp = VindictiveVampireFactory.Create(_alice);
        vamp.SetZone(ZoneType.Battlefield);

        var enemy = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        enemy.SetOwner(_bob);
        enemy.SetController(_bob);

        var trigger = vamp.Abilities.OfType<TriggeredAbility>().Single();
        var dies = new CardMovedEvent(enemy, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(dies, trigger).Should().BeFalse(
            "youControlOnly excludes an opponent's creature dying (CR 109.5)");
    }

    [Fact]
    public void VindictiveVampire_SelfDies_DoesNotTrigger()
    {
        var vamp = VindictiveVampireFactory.Create(_alice);
        vamp.SetZone(ZoneType.Battlefield);

        var trigger = vamp.Abilities.OfType<TriggeredAbility>().Single();
        var dies = new CardMovedEvent(vamp, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(dies, trigger).Should().BeFalse(
            "'another creature' excludes the Vampire's own death (no includeSelf)");
    }

    [Fact]
    public void VindictiveVampire_AnotherCreatureYouControlDies_FiresTrigger()
    {
        // Fire-only assertion: the each-opponent ping is a group effect (CR
        // 608.2) that enumerates opponents off the resolution context's Game,
        // which the bare TriggerManager/Stack path does not supply — the same
        // posture the Corpse Knight factory tests take (the effect verbs are
        // covered with a real context in JsonOpponentScopedDrainTests). Here we
        // assert the death trigger fires correctly off the live event.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var vamp = VindictiveVampireFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vamp);
        vamp.SetZone(ZoneType.Battlefield);
        var trigger = vamp.Abilities.OfType<TriggeredAbility>().Single();
        triggers.RegisterTriggeredAbility(trigger);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bus.Publish(new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard));

        triggers.PendingCount.Should().Be(1,
            "another creature you control dying fires Vindictive Vampire (CR 603.6e)");
    }
}
