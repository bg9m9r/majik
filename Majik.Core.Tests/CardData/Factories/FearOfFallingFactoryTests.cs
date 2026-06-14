using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// Tests for <see cref="FearOfFallingFactory"/>.
///
/// Fear of Falling — {3}{U}{U} Enchantment Creature — Nightmare 4/4:
///   "Flying
///    Whenever this creature attacks, target creature defending player
///    controls gets -2/-0 and loses flying until your next turn."
/// </summary>
[Trait("Color", "U")]
public class FearOfFallingFactoryTests
{
    [Fact]
    public void FearOfFalling_IsBlueNightmareEnchantmentCreature_4_4_WithFlying()
    {
        var alice = new Player("Alice", 20);
        var card = FearOfFallingFactory.Create(alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Fear of Falling");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Enchantment).Should().BeTrue("Enchantment Creature (CR 205.2a)");
        card.HasSubtype(CardSubtype.Nightmare).Should().BeTrue();
        card.BasePower.Should().Be(4);
        card.BaseToughness.Should().Be(4);
        card.ManaCostValue.TotalValue.Should().Be(5, "{3}{U}{U} is mana value 5");
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.HasEffectiveKeyword("Flying").Should().BeTrue("Fear of Falling has flying (CR 702.9)");
    }

    [Fact]
    public void Attack_DebuffsDefendingPlayersCreature_Minus2_0_AndRemovesFlying()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = FearOfFallingFactory.Create(alice, triggers, bus);
        alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.ClearSummoningSickness();

        // Bob's flying creature — the debuff target. Wire a live effects
        // service so the layer system computes its P/T + keyword set.
        var svc = new ContinuousEffectsService();
        var target = new Creature("Sky Bear", "{1}{U}", 3, 3,
            subtypes: new[] { CardSubtype.Bear }) { Owner = bob, Controller = bob };
        target.AddAbility(new KeywordAbility("Flying", target, bob));
        target.ActiveEffects = svc;
        bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        target.Power.Should().Be(3);
        target.HasEffectiveKeyword("Flying").Should().BeTrue("printed flying before the debuff");

        // Fear of Falling attacks Bob (the defending player).
        bus.Publish(new CreatureAttacksEvent(card, bob));
        triggers.PendingCount.Should().Be(1, "the attacks trigger fired");

        ResolveTriggersTargeting(triggers, stack, alice, target);

        target.Power.Should().Be(1, "-2/-0 debuff applied (3 power -> 1)");
        target.Toughness.Should().Be(3, "-2/-0 leaves toughness unchanged");
        target.HasEffectiveKeyword("Flying").Should().BeFalse("lost flying (CR 613 Layer 6)");
    }

    [Fact]
    public void Debuff_ExpiresOnControllersNextTurn()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = FearOfFallingFactory.Create(alice, triggers, bus);
        alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.ClearSummoningSickness();

        var svc = new ContinuousEffectsService();
        var target = new Creature("Sky Bear", "{1}{U}", 3, 3) { Owner = bob, Controller = bob };
        target.AddAbility(new KeywordAbility("Flying", target, bob));
        target.ActiveEffects = svc;
        bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        bus.Publish(new CreatureAttacksEvent(card, bob));
        ResolveTriggersTargeting(triggers, stack, alice, target);

        target.Power.Should().Be(1, "debuff active");
        target.HasEffectiveKeyword("Flying").Should().BeFalse("debuff active");

        // Bob's (the defending player's) turn-start must NOT lift the debuff —
        // it lasts until the CONTROLLER's (Alice's) next turn (CR 702).
        bus.Publish(new TurnStartedEvent(bob, 2));
        target.Power.Should().Be(1, "still debuffed on the defending player's turn");
        target.HasEffectiveKeyword("Flying").Should().BeFalse("still no flying on the defending player's turn");

        // Alice's next turn-start — the debuff expires.
        bus.Publish(new TurnStartedEvent(alice, 3));
        target.Power.Should().Be(3, "debuff lifted on controller's next turn");
        target.HasEffectiveKeyword("Flying").Should().BeTrue("flying restored on controller's next turn");
    }

    // Resolve the pending attacks trigger, choosing the supplied creature as
    // the single target for the "target creature defending player controls"
    // request before executing the effect.
    private static void ResolveTriggersTargeting(
        TriggerManager triggers, Majik.Core.Stack.Stack stack, Player active, Creature chosenTarget)
    {
        triggers.PutPendingTriggersOnStack(active);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (item is TriggeredAbility ta)
            {
                if (ta.TargetRequests is { Count: > 0 })
                {
                    ta.SetChosenTargets(new List<List<object>>
                    {
                        new() { chosenTarget },
                    });
                }
                foreach (var eff in ta.Effects) eff.Execute();
            }
        }
    }
}
