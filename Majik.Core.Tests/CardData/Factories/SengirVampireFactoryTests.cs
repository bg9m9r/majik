using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SengirVampireFactory"/> (Alpha / reprints,
/// {3}{B}{B}).
///
/// Creature — Vampire 4/4. Oracle text:
///   "Flying.
///    Whenever a creature dealt damage by this creature this turn dies,
///    put a +1/+1 counter on this creature."
///
/// Covers:
/// - Identity (name, type, mana cost, P/T, Vampire subtype, Flying
///   keyword marker, owner/controller) + NamedCardFactory dispatch.
/// - Damage → death loop: a creature Sengir damaged that dies grows it.
/// - Death of a creature Sengir did NOT damage does NOT grow it.
/// - Non-combat (ability/spell) damage counts (printed "dealt damage").
/// - Per-turn scope: TurnStartedEvent clears the victim set.
/// </summary>
[Trait("Color", "B")]
public class SengirVampireFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature Victim(string name = "Grizzly Bears")
        => new(name, "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };

    private static void Die(EventBus bus, Creature c)
        => bus.Publish(new CardMovedEvent(c, ZoneType.Battlefield, ZoneType.Graveyard));

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SengirVampire_Identity_Vampire_4_4_AtCost3BB()
    {
        var sv = SengirVampireFactory.Create(_alice);

        sv.Name.Should().Be("Sengir Vampire");
        sv.ManaCost.Should().Be("{3}{B}{B}");
        sv.HasType(CardType.Creature).Should().BeTrue();
        sv.HasSubtype(CardSubtype.Vampire).Should().BeTrue("Sengir Vampire is a Vampire");
        sv.BasePower.Should().Be(4);
        sv.BaseToughness.Should().Be(4);
        sv.Owner.Should().BeSameAs(_alice);
        sv.Controller.Should().BeSameAs(_alice);

        sv.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying",
                "Flying is wired as a KeywordAbility marker");
    }

    [Fact]
    public void SengirVampire_HasFlying()
    {
        var sv = SengirVampireFactory.Create(_alice);
        CombatAbilities.HasFlying(sv).Should().BeTrue("printed Flying keyword");
    }
    // -----------------------------------------------------------------------
    // Damage → death loop
    // -----------------------------------------------------------------------

    [Fact]
    public void CreatureDamagedBySengir_ThatDies_PutsPlusOnePlusOneCounter()
    {
        var bus = new EventBus();
        var sv = SengirVampireFactory.Create(_alice, bus);
        sv.SetZone(ZoneType.Battlefield);

        var victim = Victim();

        // Sengir deals combat damage to the victim, then it dies.
        bus.Publish(new CombatDamageDealtEvent(sv, victim, 4));
        Die(bus, victim);

        sv.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "a creature Sengir Vampire damaged this turn died");
    }

    [Fact]
    public void NonCombatDamageBySengir_ThatVictimDies_StillGrowsSengir()
    {
        var bus = new EventBus();
        var sv = SengirVampireFactory.Create(_alice, bus);
        sv.SetZone(ZoneType.Battlefield);

        var victim = Victim();

        // Printed condition is "dealt damage" (CR 120) — not only combat.
        bus.Publish(new DamageDealtEvent(
            sourceCard: sv,
            sourcePlayer: null,
            targetCard: victim,
            targetPlayer: null,
            amount: 2,
            damageType: DamageType.Ability));
        Die(bus, victim);

        sv.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the printed condition is 'dealt damage', not 'dealt combat damage'");
    }

    // -----------------------------------------------------------------------
    // Negative cases
    // -----------------------------------------------------------------------

    [Fact]
    public void CreatureSengirNeverDamaged_ThatDies_DoesNotGrowSengir()
    {
        var bus = new EventBus();
        var sv = SengirVampireFactory.Create(_alice, bus);
        sv.SetZone(ZoneType.Battlefield);

        var unrelated = Victim("Llanowar Elves");

        // No damage from Sengir — it just dies.
        Die(bus, unrelated);

        sv.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "Sengir Vampire never dealt damage to this creature");
    }

    [Fact]
    public void CreatureDamagedByAnotherSource_ThatDies_DoesNotGrowSengir()
    {
        var bus = new EventBus();
        var sv = SengirVampireFactory.Create(_alice, bus);
        sv.SetZone(ZoneType.Battlefield);

        var other = new Creature("Goblin Guide", "{R}", 2, 2) { Owner = _alice, Controller = _alice };
        var victim = Victim();

        // A different creature deals the damage.
        bus.Publish(new CombatDamageDealtEvent(other, victim, 2));
        Die(bus, victim);

        sv.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the printed condition is 'dealt damage by this creature'");
    }

    [Fact]
    public void DamagedVictim_ThatDoesNotDie_DoesNotGrowSengir()
    {
        var bus = new EventBus();
        var sv = SengirVampireFactory.Create(_alice, bus);
        sv.SetZone(ZoneType.Battlefield);

        var victim = Victim();

        // Damaged but survives — no death, no counter.
        bus.Publish(new CombatDamageDealtEvent(sv, victim, 1));

        sv.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the trigger fires on death, not on damage");
    }

    // -----------------------------------------------------------------------
    // Per-turn scope
    // -----------------------------------------------------------------------

    [Fact]
    public void TurnStarted_ClearsVictimSet_DeathNextTurn_DoesNotGrowSengir()
    {
        var bus = new EventBus();
        var sv = SengirVampireFactory.Create(_alice, bus);
        sv.SetZone(ZoneType.Battlefield);

        var victim = Victim();

        bus.Publish(new CombatDamageDealtEvent(sv, victim, 1));

        // A new turn begins — "this turn" scope resets (CR 514.x).
        bus.Publish(new TurnStartedEvent(_bob, 2));

        // The victim dies on the later turn — no longer linked.
        Die(bus, victim);

        sv.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the damage linkage is scoped to the turn the damage was dealt");
    }
}
