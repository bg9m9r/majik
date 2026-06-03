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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BrimazKingOfOreskosFactory"/>.
///
/// Brimaz, King of Oreskos — {1}{W}{W} Legendary Creature — Cat Soldier, 3/4:
///   "Vigilance
///    Whenever Brimaz attacks, create a 1/1 white Cat Soldier creature token
///    with vigilance that's attacking.
///    Whenever Brimaz blocks a creature, create a 1/1 white Cat Soldier
///    creature token with vigilance that's blocking that creature."
///
/// Covers:
/// - Identity: {1}{W}{W} 3/4 Legendary white Cat Soldier, mana value 3.
/// - Attack trigger: attacking mints a 1/1 white Cat Soldier (vigilance) token
///   that is tapped AND attacking the same defender.
/// - Block trigger: blocking an attacker mints a 1/1 white Cat Soldier
///   (vigilance) token that is blocking THAT same attacker.
/// </summary>
[Trait("Color", "W")]
public class BrimazKingOfOreskosFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Brimaz_IsLegendaryWhiteCatSoldier_3_4_ManaValue3_WithVigilance()
    {
        var alice = new Player("Alice", 20);
        var card = BrimazKingOfOreskosFactory.Create(alice);

        card.Should().BeOfType<Creature>();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.Name.Should().Be("Brimaz, King of Oreskos");
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(4);
        card.ManaCostValue.TotalValue.Should().Be(3, "{1}{W}{W} is mana value 3");
        card.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        CombatAbilities.HasVigilance(card).Should().BeTrue("Brimaz has Vigilance");
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    // -----------------------------------------------------------------------
    // Attack trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void OnAttack_CreatesTappedAndAttackingCatSoldierTokenWithVigilance()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);

        var brimaz = BrimazKingOfOreskosFactory.Create(alice, triggers, combat);
        alice.Zones.Battlefield.AddCard(brimaz);
        brimaz.SetZone(ZoneType.Battlefield);
        brimaz.ClearSummoningSickness();

        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(brimaz, targetPlayer: bob),
        });
        eventBus.Publish(new CreatureAttacksEvent(brimaz, bob));

        ResolvePendingTriggers(triggers, stack, alice);

        var tokens = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Cat) && c.HasSubtype(CardSubtype.Soldier))
            .ToList();

        tokens.Should().HaveCount(1, "the attack trigger creates one Cat Soldier token");
        var token = tokens[0];
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        CardColors.GetColors(token).Should().Contain(ManaColor.White, "white Cat Soldier");
        CombatAbilities.HasVigilance(token).Should().BeTrue("token has vigilance");
        token.IsTapped.Should().BeTrue("token enters tapped and attacking");

        // The token is in the current combat attacking the same defender.
        var attacker = combat.CurrentCombat!.Attackers
            .FirstOrDefault(a => ReferenceEquals(a.Creature, token));
        attacker.Should().NotBeNull("the token is spliced into combat attacking");
        attacker!.TargetPlayer.Should().BeSameAs(bob,
            "the token attacks the same defender as Brimaz");
    }

    // -----------------------------------------------------------------------
    // Block trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void OnBlock_CreatesCatSoldierTokenBlockingThatAttacker()
    {
        // Bob is the active/attacking player; Alice controls Brimaz and blocks.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);

        var brimaz = BrimazKingOfOreskosFactory.Create(alice, triggers, combat);
        alice.Zones.Battlefield.AddCard(brimaz);
        brimaz.SetZone(ZoneType.Battlefield);
        brimaz.ClearSummoningSickness();

        // Bob's attacker.
        var ogre = new Creature("Ogre", "{2}{R}", 4, 2);
        ogre.SetOwner(bob);
        ogre.SetController(bob);
        bob.Zones.Battlefield.AddCard(ogre);
        ogre.SetZone(ZoneType.Battlefield);
        ogre.ClearSummoningSickness();

        combat.StartCombat(bob);
        combat.DeclareAttackers(bob, new[]
        {
            new AttackerDeclaration(ogre, targetPlayer: alice),
        });

        // Alice declares Brimaz blocking the ogre.
        var ogreAttacker = combat.CurrentCombat!.Attackers
            .First(a => ReferenceEquals(a.Creature, ogre));
        combat.DeclareBlockers(alice, new[]
        {
            new BlockerDeclaration(brimaz, ogreAttacker),
        });

        // DeclareBlockers publishes BlockersDeclaredEvent → the block trigger fires.
        ResolvePendingTriggers(triggers, stack, alice);

        var tokens = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Cat) && c.HasSubtype(CardSubtype.Soldier))
            .ToList();

        tokens.Should().HaveCount(1, "the block trigger creates one Cat Soldier token");
        var token = tokens[0];
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        CardColors.GetColors(token).Should().Contain(ManaColor.White, "white Cat Soldier");
        CombatAbilities.HasVigilance(token).Should().BeTrue("token has vigilance");

        // The token is blocking THAT same attacker (the ogre).
        var blockers = ogreAttacker.Blockers.Select(b => b.Creature).ToList();
        blockers.Should().Contain(token,
            "the token is spliced in blocking the same creature Brimaz blocked");
        blockers.Should().Contain(brimaz, "Brimaz himself is still blocking");
    }

    private static void ResolvePendingTriggers(
        TriggerManager triggers, Majik.Core.Stack.Stack stack, Player controller)
    {
        triggers.PutPendingTriggersOnStack(controller);
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
    }
}
