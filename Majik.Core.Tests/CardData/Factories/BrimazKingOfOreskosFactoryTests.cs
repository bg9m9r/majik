using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BrimazKingOfOreskosFactory"/>.
///
/// Brimaz, King of Oreskos — {1}{W}{W} Legendary Creature — Cat Soldier 3/4:
///   "Vigilance
///    Whenever Brimaz attacks, create a 1/1 white Cat Soldier creature token
///    with vigilance that's attacking.
///    Whenever Brimaz blocks a creature, create a 1/1 white Cat Soldier
///    creature token with vigilance that's blocking that creature."
/// </summary>
[Trait("Color", "W")]
public class BrimazKingOfOreskosFactoryTests
{
    [Fact]
    public void Brimaz_IsLegendaryWhiteCatSoldier_3_4_WithVigilance()
    {
        var alice = new Player("Alice", 20);
        var card = BrimazKingOfOreskosFactory.Create(alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Brimaz, King of Oreskos");
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(4);
        card.ManaCostValue.TotalValue.Should().Be(3, "{1}{W}{W} is mana value 3");
        card.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        CombatAbilities.HasVigilance(card).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
    }

    [Fact]
    public void Brimaz_HasAttackTriggerAndBlockTrigger()
    {
        var alice = new Player("Alice", 20);
        var card = BrimazKingOfOreskosFactory.Create(alice);

        card.Abilities.OfType<TriggeredAbility>()
            .Count(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>)
            .Should().Be(1, "the attack token rider");
        card.Abilities.OfType<TriggeredAbility>()
            .Count(t => t.Condition is EventTriggerCondition<CreatureBlocksEvent>)
            .Should().Be(1, "the block token rider");
    }

    [Fact]
    public void OnAttack_CreatesOneCatSoldierTokenAttacking()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var combat = new CombatManager(bus);

        var brimaz = BrimazKingOfOreskosFactory.Create(alice, triggers, combat, zones: null);
        alice.Zones.Battlefield.AddCard(brimaz);
        brimaz.SetZone(ZoneType.Battlefield);
        brimaz.ClearSummoningSickness();

        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(brimaz, targetPlayer: bob),
        });
        bus.Publish(new CreatureAttacksEvent(brimaz, bob));

        ResolveTriggers(triggers, stack, alice);

        var tokens = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Cat))
            .ToList();

        tokens.Should().HaveCount(1, "the attack rider creates one 1/1 Cat Soldier");
        var token = tokens[0];
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        CombatAbilities.HasVigilance(token).Should().BeTrue("token has vigilance");
        CardColors.GetColors(token).Should().Contain(ManaColor.White);

        combat.CurrentCombat!.Attackers.Select(a => a.Creature).Should().Contain(token,
            "the token enters attacking");
    }

    [Fact]
    public void OnBlock_CreatesTokenBlockingTheSameAttacker()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var sba = new Majik.Core.Rules.StateBasedActions(bus);
        var combat = new CombatManager(bus, sba);

        // Bob attacks with an Ogre; Alice's Brimaz blocks it.
        var ogre = new Creature("Ogre", "{2}{R}", 3, 3) { Owner = bob, Controller = bob };
        ogre.SetZone(ZoneType.Battlefield);
        ogre.ClearSummoningSickness();

        var brimaz = BrimazKingOfOreskosFactory.Create(alice, triggers, combat, zones: null);
        alice.Zones.Battlefield.AddCard(brimaz);
        brimaz.SetZone(ZoneType.Battlefield);
        brimaz.ClearSummoningSickness();

        combat.StartCombat(bob);
        combat.DeclareAttackers(bob, new[]
        {
            new AttackerDeclaration(ogre, targetPlayer: alice),
        });
        var ogreAttacker = combat.CurrentCombat!.Attackers.Single();

        combat.DeclareBlockers(alice, new[]
        {
            new BlockerDeclaration(brimaz, ogreAttacker),
        });

        // CR 509.1h — the per-blocker event the production CombatFlow publishes.
        bus.Publish(new CreatureBlocksEvent(brimaz, ogre));

        ResolveTriggers(triggers, stack, alice);

        var tokens = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Cat))
            .ToList();

        tokens.Should().HaveCount(1, "the block rider creates one 1/1 Cat Soldier");
        var token = tokens[0];

        // The token is blocking the SAME attacker Brimaz blocked.
        ogreAttacker.Blockers.Select(b => b.Creature).Should().Contain(token,
            "the token blocks that creature (CR 509.1h)");
        token.IsTapped.Should().BeFalse("blocking does not tap");
    }

    [Fact]
    public void Brimaz_DispatchesThroughNamedFactory()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Brimaz, King of Oreskos", alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Brimaz, King of Oreskos");
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
