using System;
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
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="HanweirGarrisonFactory"/>.
///
/// Hanweir Garrison — {2}{R} Creature — Human Soldier, 2/3:
///   "Whenever this creature attacks, create two 1/1 red Human creature
///    tokens that are tapped and attacking. (Melds with Hanweir Battlements.)"
///
/// Token rider is the same shape as Hero of Bladehold's
/// (<see cref="HeroOfBladeholdFactory"/>) second attack trigger, minus the
/// battle-cry line — two tapped &amp; attacking tokens via
/// <see cref="CombatManager.AddTappedAndAttackingToken"/> (CR 508.3g).
/// Differences: red Human tokens (not white Soldier) and 2/3 stats.
/// The meld half is not modelled (deferred — no meld mechanic in v1).
/// </summary>
[Trait("Color", "R")]
public class HanweirGarrisonFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void HanweirGarrison_IsRedHumanSoldier_2_3_ManaValue3()
    {
        var alice = new Player("Alice", 20);
        var card = HanweirGarrisonFactory.Create(alice);

        card.Should().BeOfType<Creature>();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Name.Should().Be("Hanweir Garrison");
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(3);
        card.ManaCostValue.TotalValue.Should().Be(3, "{2}{R} is mana value 3");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red, "red from the {R} pip");
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }
    [Fact]
    public void HanweirGarrison_HasExactlyOneAttackTrigger()
    {
        var alice = new Player("Alice", 20);
        var card = HanweirGarrisonFactory.Create(alice);

        // Single "Whenever this creature attacks" trigger (no battle cry).
        card.Abilities.OfType<TriggeredAbility>()
            .Count(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>)
            .Should().Be(1, "the only triggered ability is the token rider");
    }

    // -----------------------------------------------------------------------
    // Token rider: two 1/1 red Human tokens, tapped and attacking.
    // -----------------------------------------------------------------------

    [Fact]
    public void OnAttack_CreatesTwoTappedAndAttackingRedHumanTokens()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);

        var garrison = HanweirGarrisonFactory.Create(
            alice,
            triggers: triggers,
            combat: combat);
        alice.Zones.Battlefield.AddCard(garrison);
        garrison.SetZone(ZoneType.Battlefield);
        garrison.ClearSummoningSickness();

        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(garrison, targetPlayer: bob),
        });
        eventBus.Publish(new CreatureAttacksEvent(garrison, bob));

        ResolveTriggers(triggers, stack, alice);

        var humans = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Human))
            .ToList();

        humans.Should().HaveCount(2, "the rider creates two 1/1 Human tokens");
        humans.Should().AllSatisfy(s =>
        {
            s.BasePower.Should().Be(1);
            s.BaseToughness.Should().Be(1);
            CardColors.GetColors(s).Should().Contain(ManaColor.Red, "red Humans");
            s.IsTapped.Should().BeTrue("tokens enter tapped");
        });

        var attackingCreatures = combat.CurrentCombat!.Attackers
            .Select(a => a.Creature)
            .ToList();
        foreach (var h in humans)
        {
            attackingCreatures.Should().Contain(h, "tokens enter attacking");
        }
        combat.CurrentCombat.Attackers
            .Where(a => humans.Contains(a.Creature))
            .Should().AllSatisfy(a =>
                a.TargetPlayer.Should().BeSameAs(bob,
                    "tokens attack the same defender as Hanweir Garrison"));
    }

    [Fact]
    public void TokenRider_OnlyTriggersForGarrisonItself_NotOtherAttackers()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var garrison = HanweirGarrisonFactory.Create(alice);
        garrison.SetZone(ZoneType.Battlefield);

        var trigger = garrison.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

        trigger.IsTriggered(new CreatureAttacksEvent(garrison, bob)).Should().BeTrue(
            "CR 508.1f per-attacker self-match.");

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(alice);
        other.SetController(alice);
        other.SetZone(ZoneType.Battlefield);
        trigger.IsTriggered(new CreatureAttacksEvent(other, bob)).Should().BeFalse(
            "the attack trigger only fires for Hanweir Garrison itself.");
    }

    private static void ResolveTriggers(
        TriggerManager triggers, Majik.Core.Stack.Stack stack, Player active)
    {
        triggers.PutPendingTriggersOnStack(active);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (item is TriggeredAbility ta)
            {
                foreach (var eff in ta.Effects) eff.Execute();
            }
        }
    }
}
