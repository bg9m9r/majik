using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Marshal of the Lost (Tarkir: Dragonstorm, {2}{W}{B},
/// Creature — Orc Warrior 3/3). Oracle text (verified against Scryfall):
///   "Deathtouch
///    Whenever you attack, target creature gets +X/+X until end of turn,
///    where X is the number of attacking creatures."
///
/// Covers ONLY the card's unique behaviour plus a single identity assert:
///   - Identity: name, {2}{W}{B}, Orc Warrior, 3/3, Deathtouch keyword.
///   - Attack trigger: on AttackersDeclaredEvent by the controller, the
///     target creature gets +X/+X until end of turn where X = number of
///     attacking creatures (CR 508.1 / 613.4).
///   - Attack trigger does NOT fire on an opponent's attack (CR 109.5).
///
/// NOTE: dispatch + well-formedness are asserted for every implemented card
/// by CardFactoryContractTests — this suite does NOT re-test those.
/// </summary>
[Trait("Color", "M")]
public class MarshalOfTheLostFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature NewCreature(Player controller, string name, int p = 1, int t = 1)
    {
        var creature = new Creature(name, "{R}", p, t);
        creature.SetOwner(controller);
        creature.SetController(controller);
        return creature;
    }

    private static Majik.Core.Combat.Combat AttackWith(
        Player attacker, Player defender, params Creature[] creatures)
    {
        var combat = new Majik.Core.Combat.Combat(attacker, defender);
        foreach (var c in creatures)
            combat.AddAttacker(new Attacker(c, defender));
        return combat;
    }

    [Fact]
    public void Marshal_Identity_OrcWarrior_3_3_AtCost2WB_WithDeathtouch()
    {
        var card = MarshalOfTheLostFactory.Create(_alice);

        card.Name.Should().Be("Marshal of the Lost");
        card.ManaCost.Should().Be("{2}{W}{B}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Orc).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(3);
        card.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword.Equals("Deathtouch", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("Marshal of the Lost has Deathtouch");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Marshal_HasAttackTrigger()
    {
        var card = MarshalOfTheLostFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void AttackTrigger_PumpsTargetByNumberOfAttackers_UntilEndOfTurn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        // Three attackers → X = 3.
        var marshal = MarshalOfTheLostFactory.Create(
            _alice, effects, triggers,
            targetResolver: combat => _target!);
        marshal.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(marshal);
        marshal.SetZone(ZoneType.Battlefield);

        var atk2 = NewCreature(_alice, "Goblin", 1, 1);
        var atk3 = NewCreature(_alice, "Wolf", 2, 2);
        _target = NewCreature(_alice, "TargetBear", 2, 2);
        _target.ActiveEffects = effects;

        // Marshal + 2 others all attack → 3 attacking creatures.
        var combat = AttackWith(_alice, _bob, marshal, atk2, atk3);
        bus.Publish(new AttackersDeclaredEvent(combat));

        triggers.PendingCount.Should().Be(1, "the attack trigger fires when you attack");

        var trigger = card_AttackTrigger(marshal);
        foreach (var e in trigger.Effects) e.Execute();

        _target.Power.Should().Be(2 + 3, "+X/+X where X = 3 attacking creatures");
        _target.Toughness.Should().Be(2 + 3, "+X/+X where X = 3 attacking creatures");

        // CR 514.2 — the +X/+X buff expires at end of turn (cleanup step).
        effects.ExpireEndOfTurn();
        _target.Power.Should().Be(2, "the +X/+X buff expires at end of turn");
        _target.Toughness.Should().Be(2, "the +X/+X buff expires at end of turn");
    }

    [Fact]
    public void AttackTrigger_OpponentAttacks_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var marshal = MarshalOfTheLostFactory.Create(_alice, effects, triggers);
        _alice.Zones.Battlefield.AddCard(marshal);
        marshal.SetZone(ZoneType.Battlefield);

        var bobBear = NewCreature(_bob, "BobBear", 2, 2);
        var combat = AttackWith(_bob, _alice, bobBear);
        bus.Publish(new AttackersDeclaredEvent(combat));

        triggers.PendingCount.Should().Be(0,
            "'Whenever you attack' only fires when Marshal's controller is the attacker");
    }

    private Creature? _target;

    private static TriggeredAbility card_AttackTrigger(Creature card)
        => card.Abilities.OfType<TriggeredAbility>().Single();
}
