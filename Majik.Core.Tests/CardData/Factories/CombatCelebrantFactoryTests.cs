using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="CombatCelebrantFactory"/>.
///
/// Combat Celebrant — {2}{R} Creature — Human Warrior 4/1:
///   "If this creature hasn't been exerted this turn, you may exert it as it
///    attacks. When you do, untap all other creatures you control and after
///    this phase, there is an additional combat phase. (An exerted creature
///    won't untap during your next untap step.)"
/// </summary>
[Trait("Color", "R")]
public class CombatCelebrantFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void CombatCelebrant_IsRedHumanWarrior_4_1()
    {
        var card = CombatCelebrantFactory.Create(_alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Combat Celebrant");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        card.BasePower.Should().Be(4);
        card.BaseToughness.Should().Be(1);
        card.ManaCostValue.TotalValue.Should().Be(3, "{2}{R} is mana value 3");
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void DispatchesThroughNamedFactory()
    {
        var card = NamedCardFactory.Create("Combat Celebrant", _alice);
        card.Should().NotBeNull();
        card!.Name.Should().Be("Combat Celebrant");
    }

    [Fact]
    public void Exert_UntapsOtherCreatures_AndEnqueuesAdditionalCombat()
    {
        using var scope = AdditionalCombatRegistryProvider.PushScope();
        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(0);

        var celebrant = CombatCelebrantFactory.Create(
            _alice, triggers: null, eventBus: null, mayExert: () => true);
        _alice.Zones.Battlefield.AddCard(celebrant);
        celebrant.SetZone(ZoneType.Battlefield);

        // An OTHER tapped creature Alice controls — must be untapped.
        var other = NonCelebrant("Grizzly Bears", _alice, 2, 2);
        _alice.Zones.Battlefield.AddCard(other);
        other.SetZone(ZoneType.Battlefield);
        other.Tap();

        // An opponent's tapped creature — must NOT be touched.
        var enemy = NonCelebrant("Goblin", _bob, 1, 1);
        _bob.Zones.Battlefield.AddCard(enemy);
        enemy.SetZone(ZoneType.Battlefield);
        enemy.Tap();

        Fire(celebrant, AttackWith(celebrant));

        other.IsTapped.Should().BeFalse("untap all OTHER creatures you control (CR 701.20a)");
        enemy.IsTapped.Should().BeTrue("opponent's creature is not untapped");
        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(1,
            "after this phase there is an additional combat phase (CR 506.4)");

        // CR 702.139c — the exert rider: won't untap next untap step.
        UntapStepRestrictions.ShouldSkipUntap(celebrant, _alice).Should().BeTrue();

        UntapStepRestrictions.RemoveAll(celebrant);
    }

    [Fact]
    public void Exert_DoesNotUntapItself()
    {
        using var scope = AdditionalCombatRegistryProvider.PushScope();

        var celebrant = CombatCelebrantFactory.Create(
            _alice, triggers: null, eventBus: null, mayExert: () => true);
        _alice.Zones.Battlefield.AddCard(celebrant);
        celebrant.SetZone(ZoneType.Battlefield);
        celebrant.Tap(); // exerted/attacking → tapped

        Fire(celebrant, AttackWith(celebrant));

        celebrant.IsTapped.Should().BeTrue(
            "Combat Celebrant untaps all OTHER creatures, not itself");

        UntapStepRestrictions.RemoveAll(celebrant);
    }

    [Fact]
    public void Decline_DoesNothing()
    {
        using var scope = AdditionalCombatRegistryProvider.PushScope();

        var celebrant = CombatCelebrantFactory.Create(
            _alice, triggers: null, eventBus: null, mayExert: () => false);
        _alice.Zones.Battlefield.AddCard(celebrant);
        celebrant.SetZone(ZoneType.Battlefield);

        var other = NonCelebrant("Grizzly Bears", _alice, 2, 2);
        _alice.Zones.Battlefield.AddCard(other);
        other.SetZone(ZoneType.Battlefield);
        other.Tap();

        Fire(celebrant, AttackWith(celebrant));

        other.IsTapped.Should().BeTrue("declining the optional exert does nothing");
        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(0,
            "no exert → no additional combat phase");
        UntapStepRestrictions.ShouldSkipUntap(celebrant, _alice).Should().BeFalse();
    }

    [Fact]
    public void Exert_OncePerTurn_SecondAttackDoesNotExertAgain()
    {
        using var scope = AdditionalCombatRegistryProvider.PushScope();

        var celebrant = CombatCelebrantFactory.Create(
            _alice, triggers: null, eventBus: null, mayExert: () => true);
        _alice.Zones.Battlefield.AddCard(celebrant);
        celebrant.SetZone(ZoneType.Battlefield);

        // First attack — exerts, enqueues one additional combat.
        Fire(celebrant, AttackWith(celebrant));
        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(1);

        // Second attack (e.g. the additional combat phase it just made) — the
        // creature has already been exerted this turn, so it must NOT exert
        // again (CR 702.139b — "If this creature hasn't been exerted this turn").
        Fire(celebrant, AttackWith(celebrant));
        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(1,
            "a creature can be exerted only once per turn (CR 702.139b)");

        UntapStepRestrictions.RemoveAll(celebrant);
    }

    [Fact]
    public void OnlyAttackingControllersTrigger_OpponentAttackDoesNothing()
    {
        using var scope = AdditionalCombatRegistryProvider.PushScope();

        var celebrant = CombatCelebrantFactory.Create(
            _alice, triggers: null, eventBus: null, mayExert: () => true);
        _alice.Zones.Battlefield.AddCard(celebrant);
        celebrant.SetZone(ZoneType.Battlefield);

        // Bob is the attacking player — Alice's "as it attacks" trigger must
        // not fire.
        var combat = new Majik.Core.Combat.Combat(attackingPlayer: _bob, defendingPlayer: _alice);
        Fire(celebrant, combat);

        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(0,
            "trigger fires only when Combat Celebrant's controller attacks");
    }

    [Fact]
    public void ExertGate_ResetsOnNewTurn()
    {
        using var scope = AdditionalCombatRegistryProvider.PushScope();

        var bus = new EventBus();
        var celebrant = CombatCelebrantFactory.Create(
            _alice, triggers: null, eventBus: bus, mayExert: () => true);
        _alice.Zones.Battlefield.AddCard(celebrant);
        celebrant.SetZone(ZoneType.Battlefield);

        Fire(celebrant, AttackWith(celebrant));
        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(1);

        // New turn resets the once-per-turn exert gate (CR 702.139b).
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 3));

        Fire(celebrant, AttackWith(celebrant));
        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(2,
            "the once-per-turn exert gate reset on the new turn");

        UntapStepRestrictions.RemoveAll(celebrant);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Creature NonCelebrant(string name, Player controller, int p, int t)
    {
        var c = new Creature(name, "{1}", p, t);
        c.SetOwner(controller);
        c.SetController(controller);
        return c;
    }

    private Majik.Core.Combat.Combat AttackWith(Creature attacker)
    {
        var combat = new Majik.Core.Combat.Combat(attackingPlayer: _alice, defendingPlayer: _bob);
        combat.AddAttacker(new Attacker(attacker, targetPlayer: _bob));
        return combat;
    }

    private void Fire(Creature celebrant, Majik.Core.Combat.Combat combat)
    {
        var trigger = celebrant.Abilities.OfType<TriggeredAbility>().Single();
        var fired = trigger.Condition.Matches(
            new AttackersDeclaredEvent(combat), trigger);
        if (!fired) return;
        foreach (var e in trigger.Effects) e.Execute();
    }
}
