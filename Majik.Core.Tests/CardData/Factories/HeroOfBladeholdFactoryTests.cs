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
/// Tests for <see cref="HeroOfBladeholdFactory"/>.
///
/// Hero of Bladehold — {2}{W}{W} Creature — Human Knight, 3/4:
///   "Battle cry (Whenever this creature attacks, each other attacking
///    creature gets +1/+0 until end of turn.)
///    Whenever this creature attacks, create two 1/1 white Soldier creature
///    tokens that are tapped and attacking."
///
/// Covers:
/// - Identity: {2}{W}{W} 3/4 white Human Knight, mana value 4, dispatch.
/// - Battle cry: on attack, each OTHER attacking creature gets +1/+0 EOT;
///   Hero itself is not pumped.
/// - Token rider: on attack, two 1/1 white Soldier tokens enter tapped AND
///   attacking the same defender as Hero.
/// </summary>
public class HeroOfBladeholdFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void HeroOfBladehold_IsWhiteHumanKnight_3_4_ManaValue4()
    {
        var alice = new Player("Alice", 20);
        var card = HeroOfBladeholdFactory.Create(alice);

        card.Should().BeOfType<Creature>();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Name.Should().Be("Hero of Bladehold");
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(4);
        card.ManaCostValue.TotalValue.Should().Be(4, "{2}{W}{W} is mana value 4");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void HeroOfBladehold_Dispatch_ResolvesViaNamedCardFactory()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Hero of Bladehold", alice);

        card.Should().BeAssignableTo<Creature>();
        card.Name.Should().Be("Hero of Bladehold");
    }

    [Fact]
    public void HeroOfBladehold_HasBattleCryKeywordMarker()
    {
        var alice = new Player("Alice", 20);
        var card = HeroOfBladeholdFactory.Create(alice);

        card.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Battle cry", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("the printed line includes Battle cry");
    }

    // -----------------------------------------------------------------------
    // Token rider: two 1/1 white Soldier tokens, tapped and attacking.
    // -----------------------------------------------------------------------

    [Fact]
    public void OnAttack_CreatesTwoTappedAndAttackingWhiteSoldierTokens()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);

        var hero = HeroOfBladeholdFactory.Create(
            alice,
            triggers: triggers,
            combat: combat,
            attackingCreaturesSource: () => combat.CurrentCombat?.Attackers
                .Select(a => a.Creature).ToList() ?? new System.Collections.Generic.List<Creature>());
        alice.Zones.Battlefield.AddCard(hero);
        hero.SetZone(ZoneType.Battlefield);
        hero.ClearSummoningSickness();

        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(hero, targetPlayer: bob),
        });
        eventBus.Publish(new CreatureAttacksEvent(hero, bob));

        ResolveTriggers(triggers, stack, alice);

        var soldiers = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Soldier))
            .ToList();

        soldiers.Should().HaveCount(2, "the rider creates two Soldier tokens");
        soldiers.Should().AllSatisfy(s =>
        {
            s.BasePower.Should().Be(1);
            s.BaseToughness.Should().Be(1);
            CardColors.GetColors(s).Should().Contain(ManaColor.White, "white Soldiers");
            s.IsTapped.Should().BeTrue("tokens enter tapped");
        });

        var attackingCreatures = combat.CurrentCombat!.Attackers
            .Select(a => a.Creature)
            .ToList();
        foreach (var s in soldiers)
        {
            attackingCreatures.Should().Contain(s, "tokens enter attacking");
        }
        combat.CurrentCombat.Attackers
            .Where(a => soldiers.Contains(a.Creature))
            .Should().AllSatisfy(a =>
                a.TargetPlayer.Should().BeSameAs(bob,
                    "tokens attack the same defender as Hero of Bladehold"));
    }

    // -----------------------------------------------------------------------
    // Battle cry: each OTHER attacking creature gets +1/+0 EOT.
    // -----------------------------------------------------------------------

    [Fact]
    public void BattleCry_PumpsEachOtherAttackingCreature_NotHeroItself()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);
        var effects = new ContinuousEffectsService();

        var hero = HeroOfBladeholdFactory.Create(
            alice,
            triggers: triggers,
            combat: combat,
            attackingCreaturesSource: () => combat.CurrentCombat?.Attackers
                .Select(a => a.Creature).ToList() ?? new System.Collections.Generic.List<Creature>());
        hero.ActiveEffects = effects;
        alice.Zones.Battlefield.AddCard(hero);
        hero.SetZone(ZoneType.Battlefield);
        hero.ClearSummoningSickness();

        // A second attacker that should be pumped by battle cry.
        var ally = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        ally.SetOwner(alice);
        ally.SetController(alice);
        ally.ActiveEffects = effects;
        alice.Zones.Battlefield.AddCard(ally);
        ally.SetZone(ZoneType.Battlefield);
        ally.ClearSummoningSickness();

        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(hero, targetPlayer: bob),
            new AttackerDeclaration(ally, targetPlayer: bob),
        });
        eventBus.Publish(new CreatureAttacksEvent(hero, bob));

        ResolveTriggers(triggers, stack, alice);

        // The other attacker is pumped +1/+0.
        ally.GetPower().Should().Be(3, "battle cry gives each other attacker +1/+0");
        ally.GetToughness().Should().Be(2, "battle cry is +1/+0 — toughness unchanged");

        // Hero itself is NOT pumped by its own battle cry ("each OTHER").
        hero.GetPower().Should().Be(3, "Hero is not pumped by its own battle cry");
        hero.GetToughness().Should().Be(4);
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
