using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PeltCollectorFactory"/> (Guilds of Ravnica, {G}).
///
/// Card: Pelt Collector — Creature — Elf Warrior 1/1.
/// Oracle (verified against Scryfall):
///   "Whenever another creature you control enters or dies, if that
///    creature's power is greater than this creature's, put a +1/+1
///    counter on this creature.
///    As long as this creature has three or more +1/+1 counters on it,
///    it has trample."
///
/// Covers:
/// - Identity (Creature — Elf Warrior, {G}, 1/1).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Trigger shape (one TriggeredAbility, enter-or-dies).
/// - Trigger fires + intervening-if passes when a bigger creature enters /
///   dies under control → +1/+1 counter on Pelt Collector.
/// - Trigger does NOT pass its intervening-if when the other creature's
///   power is not greater than Pelt Collector's.
/// - Trigger ignores Pelt Collector itself and opponents' creatures.
/// - Counter-threshold Trample static: no Trample below 3 counters; Trample
///   at >= 3 counters; lifts again when counters drop below 3.
/// </summary>
public class PeltCollectorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static bool ConditionFires(TriggeredAbility trigger, CardMovedEvent e) =>
        trigger.Condition.Matches(e, trigger);

    [Fact]
    public void PeltCollector_Identity()
    {
        var p = PeltCollectorFactory.Create(_alice);

        p.Name.Should().Be("Pelt Collector");
        p.ManaCost.Should().Be("{G}");
        p.HasType(CardType.Creature).Should().BeTrue();
        p.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        p.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        p.BasePower.Should().Be(1);
        p.BaseToughness.Should().Be(1);
        p.Owner.Should().BeSameAs(_alice);
        p.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PeltCollector_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Pelt Collector", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Pelt Collector");
        card.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    [Fact]
    public void PeltCollector_AttachesEnterOrDiesTrigger()
    {
        var p = PeltCollectorFactory.Create(_alice);
        p.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "single enter-or-dies counter trigger");
    }

    [Fact]
    public void PeltCollector_BiggerCreatureEnters_FiresAndAddsCounter()
    {
        var p = PeltCollectorFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);

        // A 3/3 you control enters — power 3 > Pelt Collector's 1.
        var bear = new Creature("Bear", "1G", 3, 3) { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var trigger = p.Abilities.OfType<TriggeredAbility>().Single();
        var enters = new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield);

        ConditionFires(trigger, enters).Should().BeTrue(
            "another creature you control with greater power entered (CR 603.4)");
        trigger.CanBePutOnStack().Should().BeTrue(
            "intervening-if: 3 > 1 (CR 603.4)");

        foreach (var fx in trigger.Effects) fx.Execute();

        p.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "one +1/+1 counter on Pelt Collector");
    }

    [Fact]
    public void PeltCollector_BiggerCreatureDies_Fires()
    {
        var p = PeltCollectorFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);

        var ox = new Creature("Ox", "2G", 4, 4) { Owner = _alice, Controller = _alice };
        // Death: card already stamped Graveyard at event-fire time, but the
        // Power getter still returns last-known power (CR 608.2g).
        ox.SetZone(ZoneType.Graveyard);

        var trigger = p.Abilities.OfType<TriggeredAbility>().Single();
        var dies = new CardMovedEvent(ox, ZoneType.Battlefield, ZoneType.Graveyard);

        ConditionFires(trigger, dies).Should().BeTrue(
            "another creature you control with greater power died (CR 700.4)");
        trigger.CanBePutOnStack().Should().BeTrue("intervening-if: 4 > 1");
    }

    [Fact]
    public void PeltCollector_EqualOrSmallerCreature_DoesNotPassInterveningIf()
    {
        var p = PeltCollectorFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);

        // 1/1 — power 1 is NOT greater than Pelt Collector's 1.
        var weenie = new Creature("Goblin", "R", 1, 1) { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(weenie);
        weenie.SetZone(ZoneType.Battlefield);

        var trigger = p.Abilities.OfType<TriggeredAbility>().Single();
        var enters = new CardMovedEvent(weenie, ZoneType.Hand, ZoneType.Battlefield);

        ConditionFires(trigger, enters).Should().BeFalse(
            "1 is not greater than 1 — the trigger's intervening-if fails (CR 603.4)");
    }

    [Fact]
    public void PeltCollector_IgnoresSelfAndOpponentCreatures()
    {
        var p = PeltCollectorFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);

        var trigger = p.Abilities.OfType<TriggeredAbility>().Single();

        // Pelt Collector itself entering — "another creature" excludes self.
        var selfEnter = new CardMovedEvent(p, ZoneType.Hand, ZoneType.Battlefield);
        ConditionFires(trigger, selfEnter).Should().BeFalse(
            "\"another creature\" excludes Pelt Collector itself");

        // A big creature an opponent controls — "you control" excludes it.
        var enemy = new Creature("Hill Giant", "3R", 3, 3) { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(enemy);
        enemy.SetZone(ZoneType.Battlefield);
        var enemyEnter = new CardMovedEvent(enemy, ZoneType.Hand, ZoneType.Battlefield);
        ConditionFires(trigger, enemyEnter).Should().BeFalse(
            "\"you control\" excludes opponents' creatures");
    }

    [Fact]
    public void PeltCollector_TrampleStatic_AppearsAtThreeCounters_LiftsBelow()
    {
        var svc = new ContinuousEffectsService();
        var p = PeltCollectorFactory.Create(
            _alice, triggers: null, replacements: null, eventBus: null,
            continuousEffects: svc);
        p.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);

        // 0 counters — no Trample.
        CombatAbilities.HasTrample(p).Should().BeFalse(
            "below 3 +1/+1 counters Pelt Collector has no Trample (CR 702.19)");

        // 2 counters — still no Trample.
        p.Counters.Add(CounterType.PlusOnePlusOne, 2);
        CombatAbilities.HasTrample(p).Should().BeFalse(
            "2 counters is below the 3-counter threshold");

        // 3rd counter — Trample appears.
        p.Counters.Add(CounterType.PlusOnePlusOne, 1);
        CombatAbilities.HasTrample(p).Should().BeTrue(
            "at 3 +1/+1 counters Pelt Collector has Trample (CR 613.1f / 702.19)");

        // Drop below threshold — Trample lifts.
        p.Counters.Remove(CounterType.PlusOnePlusOne, 1);
        CombatAbilities.HasTrample(p).Should().BeFalse(
            "Trample lifts once the count drops below 3 (CR 122.6)");
    }
}
