using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Rules.Sba.Checks;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 702.90 — Infect damage replacement tests. Each test wires a
/// <see cref="ReplacementBus"/> with one <see cref="InfectDamageReplacement"/>
/// registration and shoves a <see cref="DamageIntent"/> through the bus,
/// asserting on the resulting poison-counter / -1/-1-counter state.
/// </summary>
public class InfectDamageReplacementTests
{
    private static Creature MakeInfectAttacker(Player owner, string name = "Glistener Elf")
    {
        var c = new Creature(name, "G", 1, 1)
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
        };
        c.AddAbility(new KeywordAbility(InfectDamageReplacement.InfectKeyword, source: c, controller: owner));
        return c;
    }

    private static Creature MakeVanillaAttacker(Player owner, string name = "Grizzly Bears")
    {
        return new Creature(name, "1G", 2, 2)
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
        };
    }

    [Fact]
    public void InfectSource_DealingDamageToPlayer_AddsPoisonCountersAndCancelsLifeLoss()
    {
        // Glistener Elf attacks unblocked → opponent gains 1 poison, no life loss.
        var attackerPlayer = new Player("Active", 20);
        var defender = new Player("Defender", 20);
        var elf = MakeInfectAttacker(attackerPlayer);

        var bus = new ReplacementBus();
        InfectDamageReplacement.RegisterGlobal(bus);

        var intent = new DamageIntent(elf, 1, TargetPlayer: defender) { IsCombatDamage = true };
        var result = bus.Apply(intent);

        result.Should().BeNull("Infect replaces the damage with poison counters (CR 702.90b)");
        defender.PoisonCounters.Should().Be(1);
        defender.LifeTotal.Should().Be(20, "the life-loss path is replaced, not stacked");
    }

    [Fact]
    public void InfectSource_DealingMultipleDamage_AddsThatManyPoisonCounters()
    {
        // Phyrexian Crusader's 2 damage → 2 poison counters.
        var attackerPlayer = new Player("Active", 20);
        var defender = new Player("Defender", 20);
        var crusader = MakeInfectAttacker(attackerPlayer, "Phyrexian Crusader");

        var bus = new ReplacementBus();
        InfectDamageReplacement.RegisterGlobal(bus);

        var intent = new DamageIntent(crusader, 2, TargetPlayer: defender) { IsCombatDamage = true };
        bus.Apply(intent).Should().BeNull();
        defender.PoisonCounters.Should().Be(2);
    }

    [Fact]
    public void InfectSource_DealingDamageToCreature_AddsMinusOneMinusOneCountersAndCancelsMarkedDamage()
    {
        // Plague Stinger's 1 combat damage to a creature → one -1/-1 counter.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var stinger = MakeInfectAttacker(alice, "Plague Stinger");
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = bob,
            Controller = bob,
            Zone = ZoneType.Battlefield,
        };

        var bus = new ReplacementBus();
        InfectDamageReplacement.RegisterGlobal(bus);

        var intent = new DamageIntent(stinger, 1, TargetCreature: bear) { IsCombatDamage = true };
        bus.Apply(intent).Should().BeNull();

        bear.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);
        bear.Damage.Should().Be(0, "Infect replaces marked damage with counters (CR 702.90c)");
    }

    [Fact]
    public void NonInfectSource_DealingDamage_PassesThroughUnchanged()
    {
        // Regression: vanilla Grizzly Bears combat damage hits life normally.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bears = MakeVanillaAttacker(alice);

        var bus = new ReplacementBus();
        InfectDamageReplacement.RegisterGlobal(bus);

        var intent = new DamageIntent(bears, 2, TargetPlayer: bob) { IsCombatDamage = true };
        var result = bus.Apply(intent);

        result.Should().NotBeNull("non-Infect damage isn't replaced");
        result!.Amount.Should().Be(2);
        bob.PoisonCounters.Should().Be(0);
    }

    [Fact]
    public void NonInfectSource_DealingDamageToCreature_PassesThroughUnchanged()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bears = MakeVanillaAttacker(alice);
        var target = new Creature("Target", "2", 2, 2)
        {
            Owner = bob,
            Controller = bob,
            Zone = ZoneType.Battlefield,
        };

        var bus = new ReplacementBus();
        InfectDamageReplacement.RegisterGlobal(bus);

        var intent = new DamageIntent(bears, 2, TargetCreature: target) { IsCombatDamage = true };
        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(2);
        target.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0);
    }

    [Fact]
    public void InfectSource_OffBattlefield_DoesNotReplace()
    {
        // CR 702.90 — Infect only applies while the source is on the battlefield.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var elf = MakeInfectAttacker(alice);
        elf.Zone = ZoneType.Graveyard; // gone

        var bus = new ReplacementBus();
        InfectDamageReplacement.RegisterGlobal(bus);

        var intent = new DamageIntent(elf, 1, TargetPlayer: bob);
        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(1);
        bob.PoisonCounters.Should().Be(0);
    }

    [Fact]
    public void TenPoisonCountersFromInfect_TriggersPlayerLoss_OnSbaPass()
    {
        // Integration: drive enough Infect damage to push the defender to 10
        // poison counters, then run the SBA loop and assert HasLost.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var crusader = MakeInfectAttacker(alice, "Phyrexian Crusader");

        var bus = new ReplacementBus();
        InfectDamageReplacement.RegisterGlobal(bus);

        // Five swings × 2 damage = 10 poison counters.
        for (var i = 0; i < 5; i++)
        {
            bus.Apply(new DamageIntent(crusader, 2, TargetPlayer: bob) { IsCombatDamage = true });
        }

        bob.PoisonCounters.Should().Be(10);
        bob.LifeTotal.Should().Be(20, "Infect bypasses life loss entirely");

        // Drive the SBA loop directly via the PlayerLifeCheck so the test
        // doesn't depend on a full Game wiring.
        var check = new PlayerLifeCheck();
        var ctx = new Majik.Core.Rules.Sba.SbaContext(
            new List<Player> { alice, bob },
            new List<ICard>(),
            eventBus: null,
            zoneService: null,
            triggerManager: null,
            replacements: null);
        check.Execute(ctx).Should().BeTrue("10+ poison counters triggers CR 704.5c");
        bob.HasLost.Should().BeTrue();
    }

    [Fact]
    public void PlaneswalkerDamageFromInfectSource_PassesThroughUnchanged()
    {
        // CR 702.90 covers damage-to-player and damage-to-creature; the
        // Damage-to-planeswalker redirection on the printed pre-MoM oracle
        // is not in scope here. Verify the intent isn't replaced.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var elf = MakeInfectAttacker(alice);
        var jace = new Planeswalker("Jace", "2UU", 4) { Owner = bob, Controller = bob, Zone = ZoneType.Battlefield };

        var bus = new ReplacementBus();
        InfectDamageReplacement.RegisterGlobal(bus);

        var intent = new DamageIntent(elf, 2, TargetPlaneswalker: jace) { IsCombatDamage = true };
        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(2);
        bob.PoisonCounters.Should().Be(0);
    }
}
