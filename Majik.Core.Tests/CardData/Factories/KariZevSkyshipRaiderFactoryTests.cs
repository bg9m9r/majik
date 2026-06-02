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
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="KariZevSkyshipRaiderFactory"/>.
///
/// Kari Zev, Skyship Raider — {1}{R} Legendary Creature — Human Pirate, 1/3:
///   "First strike, menace
///    Whenever Kari Zev attacks, create Ragavan, a legendary 2/1 red Monkey
///    creature token. Ragavan enters tapped and attacking. Exile that token at
///    end of combat."
///
/// Token rider is the Hanweir Garrison / Hero of Bladehold shape — a
/// tapped-and-attacking token spliced into the in-progress combat via
/// <see cref="CombatManager.AddTappedAndAttackingToken"/> (CR 508.3g) — plus a
/// one-shot <see cref="PhaseStateType.EndOfCombat"/>
/// <see cref="StepStartedEvent"/> subscription that exiles the token (CR 514 /
/// delayed trigger; same EOT-subscription posture as
/// <see cref="AvatarRokuFactory"/>'s "until end of combat" rider).
/// </summary>
[Trait("Color", "R")]
public class KariZevSkyshipRaiderFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void KariZev_IsLegendaryRedHumanPirate_1_3_ManaValue2()
    {
        var alice = new Player("Alice", 20);
        var card = KariZevSkyshipRaiderFactory.Create(alice);

        card.Should().BeOfType<Creature>();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Name.Should().Be("Kari Zev, Skyship Raider");
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(3);
        card.ManaCostValue.TotalValue.Should().Be(2, "{1}{R} is mana value 2");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Pirate).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red, "red from the {R} pip");
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void KariZev_HasFirstStrikeAndMenaceKeywords()
    {
        var alice = new Player("Alice", 20);
        var card = KariZevSkyshipRaiderFactory.Create(alice);

        var keywords = card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();

        keywords.Should().Contain("First Strike", "CR 702.7");
        keywords.Should().Contain("Menace", "CR 702.111");
    }
    [Fact]
    public void KariZev_HasExactlyOneAttackTrigger()
    {
        var alice = new Player("Alice", 20);
        var card = KariZevSkyshipRaiderFactory.Create(alice);

        card.Abilities.OfType<TriggeredAbility>()
            .Count(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>)
            .Should().Be(1, "the only triggered ability is the Ragavan rider");
    }

    [Fact]
    public void AttackTrigger_OnlyFiresForKariZevItself()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var kariZev = KariZevSkyshipRaiderFactory.Create(alice);
        kariZev.SetZone(ZoneType.Battlefield);

        var trigger = kariZev.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

        trigger.IsTriggered(new CreatureAttacksEvent(kariZev, bob)).Should().BeTrue(
            "CR 508.1f per-attacker self-match.");

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(alice);
        other.SetController(alice);
        other.SetZone(ZoneType.Battlefield);
        trigger.IsTriggered(new CreatureAttacksEvent(other, bob)).Should().BeFalse(
            "the attack trigger only fires for Kari Zev itself.");
    }

    // -----------------------------------------------------------------------
    // Ragavan token rider: legendary 2/1 red Monkey, tapped and attacking,
    // exiled at end of combat.
    // -----------------------------------------------------------------------

    [Fact]
    public void OnAttack_CreatesTappedAndAttackingLegendary2_1RedMonkey()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);

        var kariZev = KariZevSkyshipRaiderFactory.Create(
            alice,
            triggers: triggers,
            combat: combat,
            eventBus: eventBus);
        alice.Zones.Battlefield.AddCard(kariZev);
        kariZev.SetZone(ZoneType.Battlefield);
        kariZev.ClearSummoningSickness();

        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(kariZev, targetPlayer: bob),
        });
        eventBus.Publish(new CreatureAttacksEvent(kariZev, bob));

        ResolveTriggers(triggers, stack, alice);

        var ragavan = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Monkey))
            .ToList();

        ragavan.Should().HaveCount(1, "the rider creates exactly one Ragavan token");
        var token = ragavan[0];
        token.Name.Should().Be("Ragavan");
        token.BasePower.Should().Be(2);
        token.BaseToughness.Should().Be(1);
        token.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Ragavan is legendary");
        CardColors.GetColors(token).Should().Contain(ManaColor.Red, "red Monkey");
        token.IsTapped.Should().BeTrue("Ragavan enters tapped");

        var attackingCreatures = combat.CurrentCombat!.Attackers
            .Select(a => a.Creature)
            .ToList();
        attackingCreatures.Should().Contain(token, "Ragavan enters attacking");
        combat.CurrentCombat.Attackers
            .Single(a => ReferenceEquals(a.Creature, token))
            .TargetPlayer.Should().BeSameAs(bob,
                "Ragavan attacks the same defender as Kari Zev");
    }

    [Fact]
    public void EndOfCombat_ExilesTheRagavanToken()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);

        var kariZev = KariZevSkyshipRaiderFactory.Create(
            alice,
            triggers: triggers,
            combat: combat,
            eventBus: eventBus);
        alice.Zones.Battlefield.AddCard(kariZev);
        kariZev.SetZone(ZoneType.Battlefield);
        kariZev.ClearSummoningSickness();

        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(kariZev, targetPlayer: bob),
        });
        eventBus.Publish(new CreatureAttacksEvent(kariZev, bob));
        ResolveTriggers(triggers, stack, alice);

        var token = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken && c.HasSubtype(CardSubtype.Monkey));

        // CR 514 / delayed trigger — "Exile that token at end of combat."
        eventBus.Publish(new StepStartedEvent(PhaseStateType.EndOfCombat, alice));

        token.Zone.Should().Be(ZoneType.Exile, "Ragavan is exiled at end of combat");
        alice.Zones.Battlefield.GetCards().Should().NotContain(token,
            "the token leaves the battlefield");
    }

    [Fact]
    public void EndOfCombat_BeforeCombat_DoesNotExileEarly()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);

        var kariZev = KariZevSkyshipRaiderFactory.Create(
            alice,
            triggers: triggers,
            combat: combat,
            eventBus: eventBus);
        alice.Zones.Battlefield.AddCard(kariZev);
        kariZev.SetZone(ZoneType.Battlefield);
        kariZev.ClearSummoningSickness();

        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(kariZev, targetPlayer: bob),
        });
        eventBus.Publish(new CreatureAttacksEvent(kariZev, bob));
        ResolveTriggers(triggers, stack, alice);

        var token = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken && c.HasSubtype(CardSubtype.Monkey));

        // A non-end-of-combat step must NOT exile the token.
        eventBus.Publish(new StepStartedEvent(PhaseStateType.Draw, alice));

        token.Zone.Should().Be(ZoneType.Battlefield,
            "the exile rider only fires on the end-of-combat step");
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
