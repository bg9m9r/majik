using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="AnimPakalThousandthMoonFactory"/>.
///
/// Anim Pakal, Thousandth Moon — {1}{R}{W} Legendary Creature — Human Soldier,
/// 1/2:
///   "Whenever you attack with one or more non-Gnome creatures, put a +1/+1
///    counter on Anim Pakal, then create X 1/1 colorless Gnome artifact creature
///    tokens that are tapped and attacking, where X is the number of +1/+1
///    counters on Anim Pakal."
///
/// The attack trigger is the whole-combat "Whenever you attack" shape of
/// <see cref="AdelineResplendentCatharFactory"/> (gated on
/// <see cref="AttackersDeclaredEvent"/> by the controller), gated additionally
/// on at least one non-Gnome attacker, with a +1/+1 counter accumulator that
/// scales the tapped &amp; attacking Gnome token count
/// (<see cref="CombatManager.AddTappedAndAttackingToken"/>, CR 508.3g).
/// </summary>
[Trait("Color", "M")]
public class AnimPakalThousandthMoonFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity — {1}{R}{W} Legendary Human Soldier 1/2, R/W.
    // -----------------------------------------------------------------------

    [Fact]
    public void AnimPakal_Identity_IsLegendaryRedWhiteHumanSoldier_1_2_ManaValue3()
    {
        var alice = new Player("Alice", 20);
        var card = AnimPakalThousandthMoonFactory.Create(alice);

        card.Should().BeOfType<Creature>();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Name.Should().Be("Anim Pakal, Thousandth Moon");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Legendary supertype");
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(2);
        card.ManaCostValue.TotalValue.Should().Be(3, "{1}{R}{W} is mana value 3");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Red, "red from the {R} pip");
        colors.Should().Contain(ManaColor.White, "white from the {W} pip");
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void AnimPakal_HasExactlyOneAttackTrigger()
    {
        var alice = new Player("Alice", 20);
        var card = AnimPakalThousandthMoonFactory.Create(alice);

        card.Abilities.OfType<TriggeredAbility>()
            .Count(t => t.Condition is EventTriggerCondition<AttackersDeclaredEvent>)
            .Should().Be(1, "the only triggered ability is the attack rider");
    }

    // -----------------------------------------------------------------------
    // Non-Gnome gate (CR 205.3).
    // -----------------------------------------------------------------------

    [Fact]
    public void NonGnomeGate_TrueWhenAttackerIsNonGnome()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var combat = new CombatManager(new EventBus());

        var anim = AnimPakalThousandthMoonFactory.Create(alice);
        anim.SetZone(ZoneType.Battlefield);
        anim.ClearSummoningSickness();

        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[] { new AttackerDeclaration(anim, targetPlayer: bob) });

        AnimPakalThousandthMoonFactory.AttackIncludesNonGnomeCreature(combat.CurrentCombat!)
            .Should().BeTrue("Anim Pakal herself is a non-Gnome creature attacker");
    }

    [Fact]
    public void NonGnomeGate_FalseWhenAllAttackersAreGnomes()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var combat = new CombatManager(new EventBus());

        var gnomeOnly = new Creature("Gnome", "", 1, 1, subtypes: new[] { CardSubtype.Gnome });
        gnomeOnly.SetOwner(alice);
        gnomeOnly.SetController(alice);
        gnomeOnly.SetZone(ZoneType.Battlefield);
        gnomeOnly.ClearSummoningSickness();

        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[] { new AttackerDeclaration(gnomeOnly, targetPlayer: bob) });

        AnimPakalThousandthMoonFactory.AttackIncludesNonGnomeCreature(combat.CurrentCombat!)
            .Should().BeFalse("an all-Gnome attack does not satisfy the non-Gnome gate");
    }

    // -----------------------------------------------------------------------
    // Counter accumulator + scaling Gnome rider.
    // -----------------------------------------------------------------------

    [Fact]
    public void OnFirstAttack_PutsOneCounter_AndCreatesOneTappedAttackingGnomeArtifactCreature()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var combat = new CombatManager(bus);

        var anim = AnimPakalThousandthMoonFactory.Create(
            alice, triggers: triggers, combat: combat, eventBus: bus, replacements: null);
        alice.Zones.Battlefield.AddCard(anim);
        anim.SetZone(ZoneType.Battlefield);
        anim.ClearSummoningSickness();

        AttackWith(combat, triggers, stack, alice, bob, anim);

        // "put a +1/+1 counter on Anim Pakal" — count is now 1.
        anim.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);

        var gnomes = Gnomes(alice);
        gnomes.Should().HaveCount(1, "X = 1 +1/+1 counter on Anim Pakal after the first attack");
        gnomes.Should().AllSatisfy(g =>
        {
            g.BasePower.Should().Be(1);
            g.BaseToughness.Should().Be(1);
            CardColors.GetColors(g).Should().BeEmpty("colorless Gnome token");
            g.HasType(CardType.Artifact).Should().BeTrue("Gnome artifact creature token");
            g.HasType(CardType.Creature).Should().BeTrue();
            g.HasSubtype(CardSubtype.Gnome).Should().BeTrue();
            g.IsTapped.Should().BeTrue("token enters tapped");
        });

        // Tokens spliced into the in-progress combat attacking the same defender.
        var attacking = combat.CurrentCombat!.Attackers.Select(a => a.Creature).ToList();
        foreach (var g in gnomes) attacking.Should().Contain(g, "tokens enter attacking");
        combat.CurrentCombat.Attackers
            .Where(a => gnomes.Contains(a.Creature))
            .Should().AllSatisfy(a =>
                a.TargetPlayer.Should().BeSameAs(bob, "tokens attack the same defender as Anim Pakal"));
    }

    [Fact]
    public void CounterAccumulates_SecondAttackCreatesTwoGnomes()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var combat = new CombatManager(bus);

        var anim = AnimPakalThousandthMoonFactory.Create(
            alice, triggers: triggers, combat: combat, eventBus: bus, replacements: null);
        alice.Zones.Battlefield.AddCard(anim);
        anim.SetZone(ZoneType.Battlefield);
        anim.ClearSummoningSickness();

        // First attack: +1 counter -> 1 Gnome.
        AttackWith(combat, triggers, stack, alice, bob, anim);
        anim.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        Gnomes(alice).Should().HaveCount(1);

        // Second attack: +1 counter (now 2) -> 2 more Gnomes (total 3 on the field).
        AttackWith(combat, triggers, stack, alice, bob, anim);
        anim.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2, "a second attack adds another +1/+1 counter");
        Gnomes(alice).Should().HaveCount(3, "X = 2 on the second attack, added to the 1 from the first");
    }

    [Fact]
    public void AllGnomeAttack_DoesNotTrigger_NoCounterNoTokens()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var combat = new CombatManager(bus);

        var anim = AnimPakalThousandthMoonFactory.Create(
            alice, triggers: triggers, combat: combat, eventBus: bus, replacements: null);
        alice.Zones.Battlefield.AddCard(anim);
        anim.SetZone(ZoneType.Battlefield);

        // Attack with a Gnome ONLY (Anim Pakal stays back). Gate must fail.
        var gnome = new Creature("Gnome", "", 1, 1, subtypes: new[] { CardSubtype.Gnome });
        gnome.SetOwner(alice);
        gnome.SetController(alice);
        gnome.SetZone(ZoneType.Battlefield);
        gnome.ClearSummoningSickness();
        alice.Zones.Battlefield.AddCard(gnome);

        AttackWith(combat, triggers, stack, alice, bob, gnome);

        anim.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "an all-Gnome attack does not satisfy the non-Gnome gate");
        // No token Gnomes minted (only the declared Gnome attacker exists, and it is not a token).
        alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Count(c => c.IsToken && c.HasSubtype(CardSubtype.Gnome))
            .Should().Be(0, "no Gnome tokens are created when the trigger does not fire");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static List<Creature> Gnomes(Player p) =>
        p.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Gnome))
            .ToList();

    private static void AttackWith(
        CombatManager combat, TriggerManager triggers, Majik.Core.Stack.Stack stack,
        Player active, Player defender, Creature attacker)
    {
        // Fresh combat each call (the prior one, if any, is ended first); untap
        // the attacker so a repeat declaration is legal (CR 508.1a — an attacker
        // taps unless it has vigilance, which Anim Pakal lacks).
        if (combat.CurrentCombat is { IsEnded: false }) combat.EndCombat();
        if (attacker.IsTapped) attacker.Untap();

        combat.StartCombat(active);
        // DeclareAttackers publishes AttackersDeclaredEvent itself, landing the
        // whole-combat "Whenever you attack" trigger as pending.
        combat.DeclareAttackers(active, new[]
        {
            new AttackerDeclaration(attacker, targetPlayer: defender),
        });
        ResolveTriggers(triggers, stack, active);
        // Combat is left LIVE so callers can inspect the spliced tapped &
        // attacking tokens on combat.CurrentCombat.
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
