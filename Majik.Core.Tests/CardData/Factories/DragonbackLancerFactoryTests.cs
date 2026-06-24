using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="DragonbackLancerFactory"/>.
///
/// Dragonback Lancer — {3}{W} Creature — Human Soldier, 3/3:
///   "Flying
///    Mobilize 1 (Whenever this creature attacks, create a tapped and attacking
///    1/1 red Warrior creature token. Sacrifice it at the beginning of the next
///    end step.)"
///
/// Covers (the card's UNIQUE behaviour + a single identity assert):
/// - Identity: {3}{W} 3/3 white Human Soldier with Flying, mana value 4.
/// - Mobilize 1: attacking creates ONE 1/1 red Warrior token that is tapped AND
///   attacking (spliced into the current combat); it is sacrificed at the next
///   end step (CR 702.170 / 508.3g / 603.7).
/// </summary>
[Trait("Color", "W")]
public class DragonbackLancerFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void DragonbackLancer_IsWhiteHumanSoldier_3_3_Flying_ManaValue4()
    {
        var alice = new Player("Alice", 20);
        var card = DragonbackLancerFactory.Create(alice);

        card.Should().BeOfType<Creature>();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Name.Should().Be("Dragonback Lancer");
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(3);
        card.ManaCostValue.TotalValue.Should().Be(4, "{3}{W} is mana value 4");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "the printed Flying line is stamped as a KeywordAbility marker (CR 702.9)");
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    // -----------------------------------------------------------------------
    // Mobilize 1: one tapped + attacking token, sacrificed at next end step.
    // -----------------------------------------------------------------------

    [Fact]
    public void Mobilize_OnAttack_CreatesOneTappedAndAttackingRedWarriorToken()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);

        var lancer = DragonbackLancerFactory.Create(alice, triggers, combat);
        alice.Zones.Battlefield.AddCard(lancer);
        lancer.SetZone(ZoneType.Battlefield);
        lancer.ClearSummoningSickness();

        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(lancer, targetPlayer: bob),
        });
        eventBus.Publish(new CreatureAttacksEvent(lancer, bob));

        triggers.PutPendingTriggersOnStack(alice);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (item is TriggeredAbility ta)
            {
                foreach (var eff in ta.Effects) eff.Execute();
            }
        }

        var warriors = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Warrior))
            .ToList();

        warriors.Should().HaveCount(1, "Mobilize 1 creates one Warrior token");
        var token = warriors[0];
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        CardColors.GetColors(token).Should().Contain(ManaColor.Red, "red Warrior");
        token.IsTapped.Should().BeTrue("the token enters tapped");

        // The token is in the current combat's attacker set against Bob.
        var attacker = combat.CurrentCombat!.Attackers
            .SingleOrDefault(a => ReferenceEquals(a.Creature, token));
        attacker.Should().NotBeNull("the token enters attacking");
        attacker!.TargetPlayer.Should().BeSameAs(bob,
            "the token attacks the same defender as Dragonback Lancer");
    }

    [Fact]
    public void Mobilize_Token_IsSacrificed_AtNextEndStep()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);

        var lancer = DragonbackLancerFactory.Create(alice, triggers, combat);
        alice.Zones.Battlefield.AddCard(lancer);
        lancer.SetZone(ZoneType.Battlefield);
        lancer.ClearSummoningSickness();

        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(lancer, targetPlayer: bob),
        });
        eventBus.Publish(new CreatureAttacksEvent(lancer, bob));

        triggers.PutPendingTriggersOnStack(alice);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (item is TriggeredAbility ta)
            {
                foreach (var eff in ta.Effects) eff.Execute();
            }
        }

        var warriors = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Warrior))
            .ToList();
        warriors.Should().HaveCount(1);

        // Fire the end step — the delayed sacrifice trigger should resolve.
        eventBus.Publish(new StepStartedEvent(StepStateType.End, alice));
        triggers.PutPendingTriggersOnStack(alice);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (item is TriggeredAbility ta)
            {
                foreach (var eff in ta.Effects) eff.Execute();
            }
            else if (item is DelayedTriggeredAbility dta)
            {
                foreach (var eff in dta.Effects) eff.Execute();
            }
        }

        var remaining = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Warrior))
            .ToList();
        remaining.Should().BeEmpty("the token is sacrificed at the next end step");
    }
}
