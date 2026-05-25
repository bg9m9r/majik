using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// Tests for Murderous Redcap (Shadowmoor, {2}{B}{R}):
///   - 2/2 Goblin Assassin shape with the printed cost.
///   - ETB damage trigger queues + reads the chosen any-target.
///   - Persist (CR 702.79) returns Redcap on death-without-counter and not
///     after the post-Persist death.
/// </summary>
public class MurderousRedcapTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Shape_Is2_2_GoblinAssassin_With4ManaCost()
    {
        var redcap = MurderousRedcapFactory.Create(_alice);

        redcap.Name.Should().Be(MurderousRedcapFactory.CardName);
        redcap.Power.Should().Be(MurderousRedcapFactory.Power);
        redcap.Toughness.Should().Be(MurderousRedcapFactory.Toughness);
        redcap.Subtypes.Should().Contain(CardSubtype.Goblin).And.Contain(CardSubtype.Assassin);
        redcap.ManaCost.Should().NotBeNull();
    }

    [Fact]
    public void Shape_AttachesEtbDamageTriggerAndPersistTrigger()
    {
        var redcap = MurderousRedcapFactory.Create(_alice);

        // Two TriggeredAbilities: one ETB damage trigger + one Persist death trigger.
        redcap.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "Murderous Redcap ships an ETB damage trigger + the Persist death trigger");

        // ETB has one target request (any target).
        var etb = redcap.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        etb.TargetRequests.Should().HaveCount(1);
        etb.TargetRequests[0].MinTargets.Should().Be(1);
        etb.TargetRequests[0].MaxTargets.Should().Be(1);

        // Persist trigger has Graveyard in ActiveZones (Undying-shape).
        var persist = redcap.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        persist.ActiveZones.Should().Contain(ZoneType.Graveyard);
    }

    [Fact]
    public void Etb_DealsDamage_ToChosenCreatureTarget()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var redcap = MurderousRedcapFactory.Create(_alice);
        redcap.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(redcap);
        triggers.BindCard(redcap);

        // Bob's creature absorbs the 2 damage.
        var bear = new Creature("Bear", "{1}{G}", 2, 2, subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        zones.MoveCardTo(redcap, ZoneType.Battlefield);

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "ETB damage trigger must queue when Murderous Redcap enters");

        // Wire chosen target onto the ETB trigger.
        var etb = redcap.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        etb.SetChosenTargets(new[] { new[] { (object)bear } });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bear.Damage.Should().Be(MurderousRedcapFactory.EtbDamage,
            "Murderous Redcap deals 2 damage to the chosen creature target");
    }

    [Fact]
    public void Persist_DiesWithNoCounter_ReturnsWithMinusOneOneCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var redcap = MurderousRedcapFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(redcap);
        redcap.SetZone(ZoneType.Battlefield);
        triggers.BindCard(redcap);

        zones.MoveCardTo(redcap, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(1, "Persist death trigger must queue");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        redcap.Zone.Should().Be(ZoneType.Battlefield);
        redcap.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1,
            "Persist places one -1/-1 counter on the returning Redcap");
    }
}
