using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
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
/// Tests for <see cref="WarrenWarleaderFactory"/>.
///
/// Warren Warleader — {2}{W}{W} Creature — Rabbit Knight, 4/4:
///   "Offspring {2} (You may pay an additional {2} as you cast this spell. If
///    you do, when this creature enters, create a 1/1 token copy of it.)
///    Whenever you attack, choose one —
///    • Create a 1/1 white Rabbit creature token that's tapped and attacking.
///    • Attacking creatures you control get +1/+1 until end of turn."
///
/// Covers only the card's unique behaviour: the Offspring keyword wiring + the
/// modal "Whenever you attack" trigger (token mode via the Adeline / Kari Zev
/// tapped-and-attacking splice; pump mode via the Honored Crop-Captain
/// per-attacker +1/+1). Dispatch / well-formedness is covered automatically by
/// CardFactoryContractTests.
/// </summary>
[Trait("Color", "W")]
public class WarrenWarleaderFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WarrenWarleader_IsWhiteRabbitKnight_4_4_ManaValue4()
    {
        var alice = new Player("Alice", 20);
        var card = WarrenWarleaderFactory.Create(alice);

        card.Should().BeOfType<Creature>();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Name.Should().Be("Warren Warleader");
        card.BasePower.Should().Be(4);
        card.BaseToughness.Should().Be(4);
        card.ManaCostValue.TotalValue.Should().Be(4, "{2}{W}{W} is mana value 4");
        card.HasSubtype(CardSubtype.Rabbit).Should().BeTrue();
        card.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White, "white from the {W}{W} pips");
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    // -----------------------------------------------------------------------
    // Offspring {2} (CR 702.169)
    // -----------------------------------------------------------------------

    [Fact]
    public void WarrenWarleader_HasOffspringKeywordAndCost()
    {
        var alice = new Player("Alice", 20);
        var card = WarrenWarleaderFactory.Create(alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Offspring", "CR 702.169");

        WarrenWarleaderFactory.OffspringCost.TotalValue.Should().Be(2, "Offspring {2}");
        WarrenWarleaderFactory.BuildOffspringCost(card).Should().BeOfType<OffspringAdditionalCost>();
    }

    // -----------------------------------------------------------------------
    // Modal attack trigger — "Whenever you attack, choose one —"
    // -----------------------------------------------------------------------

    [Fact]
    public void WarrenWarleader_HasExactlyOneAttackTrigger()
    {
        var alice = new Player("Alice", 20);
        var card = WarrenWarleaderFactory.Create(alice);

        card.Abilities.OfType<TriggeredAbility>()
            .Count(t => t.Condition is EventTriggerCondition<AttackersDeclaredEvent>)
            .Should().Be(1, "the only attack trigger is the modal 'whenever you attack' choice");
    }

    [Fact]
    public void AttackTrigger_OnlyFiresWhenControllerIsAttackingPlayer()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var combat = new CombatManager(eventBus);

        var card = WarrenWarleaderFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield); // CR 113.6 — the trigger functions only from the battlefield.

        var trigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<AttackersDeclaredEvent>);

        // Alice's combat — controller IS the attacking player.
        var aliceAttacker = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceAttacker.SetOwner(alice);
        aliceAttacker.SetController(alice);
        alice.Zones.Battlefield.AddCard(aliceAttacker);
        aliceAttacker.SetZone(ZoneType.Battlefield);
        aliceAttacker.ClearSummoningSickness();
        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(aliceAttacker, targetPlayer: bob),
        });
        trigger.IsTriggered(new AttackersDeclaredEvent(combat.CurrentCombat!))
            .Should().BeTrue("Warren Warleader's controller is the attacking player");

        // Bob's combat — Warren Warleader's controller (Alice) is NOT attacking.
        var bobBus = new EventBus();
        var bobCombat = new CombatManager(bobBus);
        var bobAttacker = new Creature("Hill Giant", "{3}{R}", 3, 3);
        bobAttacker.SetOwner(bob);
        bobAttacker.SetController(bob);
        bob.Zones.Battlefield.AddCard(bobAttacker);
        bobAttacker.SetZone(ZoneType.Battlefield);
        bobAttacker.ClearSummoningSickness();
        bobCombat.StartCombat(bob);
        bobCombat.DeclareAttackers(bob, new[]
        {
            new AttackerDeclaration(bobAttacker, targetPlayer: alice),
        });
        trigger.IsTriggered(new AttackersDeclaredEvent(bobCombat.CurrentCombat!))
            .Should().BeFalse("the trigger only fires on its controller's attack");
    }

    [Fact]
    public void DefaultMode_CreatesTappedAndAttacking1_1WhiteRabbitToken()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);

        var attacker = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        attacker.SetOwner(alice);
        attacker.SetController(alice);
        alice.Zones.Battlefield.AddCard(attacker);
        attacker.SetZone(ZoneType.Battlefield);
        attacker.ClearSummoningSickness();

        var card = WarrenWarleaderFactory.Create(alice, triggers: triggers, combat: combat);
        alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        combat.StartCombat(alice);
        // DeclareAttackers publishes AttackersDeclaredEvent, which the
        // TriggerManager auto-evaluates into the pending set.
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(attacker, targetPlayer: bob),
        });

        ResolveTriggers(triggers, stack, alice);

        // No agent is wired, so the modal pick defaults to mode 0 (the token).
        var rabbits = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Rabbit))
            .ToList();

        rabbits.Should().HaveCount(1, "the default mode creates exactly one Rabbit token");
        var token = rabbits[0];
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        CardColors.GetColors(token).Should().Contain(ManaColor.White, "white Rabbit");
        token.IsTapped.Should().BeTrue("the token enters tapped");

        var attackingCreatures = combat.CurrentCombat!.Attackers
            .Select(a => a.Creature)
            .ToList();
        attackingCreatures.Should().Contain(token, "the token enters attacking");
        combat.CurrentCombat.Attackers
            .Single(a => ReferenceEquals(a.Creature, token))
            .TargetPlayer.Should().BeSameAs(bob,
                "the token attacks the same defender as the combat");
    }

    [Fact]
    public async System.Threading.Tasks.Task PumpMode_GivesAttackingCreaturesYouControlPlusOnePlusOne()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var combat = new CombatManager(eventBus);
        var effects = new ContinuousEffectsService();

        // Two of Alice's creatures attacking; Bob's creature must NOT be pumped
        // (it isn't a creature Alice controls).
        var aliceAttacker = MakeCreature("Savannah Lions", alice, effects, 2, 1);
        var warleader = WarrenWarleaderFactory.Create(alice, triggers: null, combat: combat);
        warleader.ActiveEffects = effects;
        alice.Zones.Battlefield.AddCard(warleader);
        warleader.SetZone(ZoneType.Battlefield);
        warleader.ClearSummoningSickness();

        var bobCreature = MakeCreature("Hill Giant", bob, effects, 3, 3);

        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(aliceAttacker, targetPlayer: bob),
            new AttackerDeclaration(warleader, targetPlayer: bob),
        });

        // Fire the condition to capture the combat, then resolve with an agent
        // that picks mode 1 (the +1/+1 pump).
        var trigger = warleader.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<AttackersDeclaredEvent>);

        trigger.IsTriggered(new AttackersDeclaredEvent(combat.CurrentCombat!))
            .Should().BeTrue();

        var agent = new Majik.Core.Players.Agents.ScriptedAgent();
        agent.QueueMode(1); // mode index 1 = "Attacking creatures you control get +1/+1"
        var game = new GameContext(
            alice, new[] { alice, bob }, alice, 1, StepStateType.BeginningOfCombat, stack);

        var ctx = new ResolutionContext(
            Controller: alice,
            Agent: agent,
            Game: game,
            ChosenTargets: System.Array.Empty<System.Collections.Generic.IReadOnlyList<object>>());

        foreach (var eff in trigger.Effects)
        {
            await eff.ExecuteAsync(ctx);
        }

        aliceAttacker.GetPower().Should().Be(3, "Savannah Lions 2/1 +1/+1 = 3/2");
        aliceAttacker.GetToughness().Should().Be(2);
        warleader.GetPower().Should().Be(5, "Warren Warleader 4/4 +1/+1 = 5/5 (it is attacking)");
        warleader.GetToughness().Should().Be(5);
        bobCreature.GetPower().Should().Be(3, "Bob's creature is not 'attacking creatures you control'");
        bobCreature.GetToughness().Should().Be(3);
    }

    private static Creature MakeCreature(
        string name, Player owner, ContinuousEffectsService effects, int p, int t)
    {
        var c = new Creature(name, "{1}", p, t)
        {
            Owner = owner,
            Controller = owner,
            ActiveEffects = effects,
        };
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        c.ClearSummoningSickness();
        return c;
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
