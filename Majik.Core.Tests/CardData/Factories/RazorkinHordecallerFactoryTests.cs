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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Razorkin Hordecaller (Duskmourn: House of Horror, {4}{R},
/// Creature — Human Clown Berserker 4/4). Oracle text (verified against
/// Scryfall):
///   "Haste
///    Whenever you attack, create a 1/1 red Gremlin creature token."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity (name, {4}{R}, 4/4, Human Clown Berserker, Haste).
///   - Attack trigger: on AttackersDeclaredEvent by the controller, a 1/1 red
///     Gremlin creature token is created under the controller.
///   - Attack trigger does NOT fire on an opponent's attack.
///
/// (Dispatch + well-formedness are covered by CardFactoryContractTests.)
/// </summary>
[Trait("Color", "R")]
public class RazorkinHordecallerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature NewAttacker(Player controller, string name, int p = 1, int t = 1)
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
    public void Razorkin_Identity_HumanClownBerserker_4_4_AtCost4R_WithHaste()
    {
        var card = RazorkinHordecallerFactory.Create(_alice);

        card.Name.Should().Be("Razorkin Hordecaller");
        card.ManaCost.Should().Be("{4}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Clown).Should().BeTrue();
        card.HasSubtype(CardSubtype.Berserker).Should().BeTrue();
        card.BasePower.Should().Be(4);
        card.BaseToughness.Should().Be(4);
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Haste", "Razorkin Hordecaller has Haste");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Razorkin_HasOneAttackTrigger()
    {
        var card = RazorkinHordecallerFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void AttackTrigger_ControllerAttacks_CreatesOneRedGremlinToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = RazorkinHordecallerFactory.Create(_alice, triggers, zones: null);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var bear = NewAttacker(_alice, "Bear", 2, 2);
        var combat = AttackWith(_alice, _bob, bear);
        bus.Publish(new AttackersDeclaredEvent(combat));

        triggers.PendingCount.Should().Be(1, "the attack trigger fires when you attack");

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Gremlin")
            .ToList();

        tokens.Should().HaveCount(1, "one 1/1 red Gremlin token is created on attack");
        var gremlin = tokens.Single();
        gremlin.BasePower.Should().Be(1);
        gremlin.BaseToughness.Should().Be(1);
        gremlin.HasSubtype(CardSubtype.Gremlin).Should().BeTrue();
        Majik.Core.Cards.CardColors.GetColors(gremlin)
            .Should().Contain(ManaColor.Red, "the Gremlin token is red");
        gremlin.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AttackTrigger_OpponentAttacks_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = RazorkinHordecallerFactory.Create(_alice, triggers, zones: null);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var bobBear = NewAttacker(_bob, "BobBear", 2, 2);
        var combat = AttackWith(_bob, _alice, bobBear);
        bus.Publish(new AttackersDeclaredEvent(combat));

        triggers.PendingCount.Should().Be(0,
            "'Whenever you attack' only fires when Razorkin's controller is the attacker");
    }
}
