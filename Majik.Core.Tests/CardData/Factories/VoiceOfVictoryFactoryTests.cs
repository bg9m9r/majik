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
using Majik.Core.Rules;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="VoiceOfVictoryFactory"/>.
///
/// Voice of Victory — {1}{W} Creature — Human Bard, 2/2:
///   "Mobilize 2 (Whenever this creature attacks, create two tapped and
///    attacking 1/1 red Warrior creature tokens. Sacrifice them at the
///    beginning of the next end step.)
///    Your opponents can't cast spells during your turn."
///
/// Covers:
/// - Identity: {1}{W} 2/2 white Human Bard, mana value 2, dispatch.
/// - Static: opponents can't cast spells on the controller's turn; they can
///   on their own turn.
/// - Mobilize 2: attacking creates two 1/1 red Warrior tokens that are tapped
///   AND attacking (spliced into the current combat); they are sacrificed at
///   the next end step.
/// </summary>
public class VoiceOfVictoryFactoryTests : IDisposable
{
    public VoiceOfVictoryFactoryTests() => CastingRestrictions.Clear();

    public void Dispose() => CastingRestrictions.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void VoiceOfVictory_IsWhiteHumanBard_2_2_ManaValue2()
    {
        var alice = new Player("Alice", 20);
        var card = VoiceOfVictoryFactory.Create(alice);

        card.Should().BeOfType<Creature>();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Name.Should().Be("Voice of Victory");
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.ManaCostValue.TotalValue.Should().Be(2, "{1}{W} is mana value 2");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Bard).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void VoiceOfVictory_Dispatch_ResolvesViaNamedCardFactory()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Voice of Victory", alice);

        card.Should().BeAssignableTo<Creature>();
        card.Name.Should().Be("Voice of Victory");
    }

    // -----------------------------------------------------------------------
    // Static: "Your opponents can't cast spells during your turn."
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_OpponentCannotCastSpell_DuringControllersTurn()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var eventBus = new EventBus();

        var card = VoiceOfVictoryFactory.Create(
            alice,
            opponentResolver: () => new[] { bob },
            eventBus: eventBus,
            triggers: null,
            combat: null);
        alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Alice's turn begins → Bob is restricted.
        eventBus.Publish(new TurnStartedEvent(alice, 1));

        CastingRestrictions.CannotCastAnySpell(bob).Should().BeTrue(
            "opponents can't cast spells during the controller's turn");

        // A cast attempt by Bob is rejected by the validator.
        var validator = new ActionValidator();
        var spell = new Creature("Bear", "{1}{G}", 2, 2);
        spell.SetOwner(bob);
        var action = new CastSpellAction(
            spell, bob, sorcerySpeedAvailable: true, fromZone: ZoneType.Hand);
        validator.ValidateAction(action).IsValid.Should().BeFalse(
            "even a creature spell is blocked by the total cast restriction");
    }

    [Fact]
    public void Static_OpponentCanCastSpell_OnTheirOwnTurn()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var eventBus = new EventBus();

        var card = VoiceOfVictoryFactory.Create(
            alice,
            opponentResolver: () => new[] { bob },
            eventBus: eventBus,
            triggers: null,
            combat: null);
        alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Alice's turn begins (Bob restricted), then ends (restriction lifts).
        eventBus.Publish(new TurnStartedEvent(alice, 1));
        eventBus.Publish(new TurnEndedEvent(alice, 1));

        // Bob's turn begins — restriction must NOT apply to Bob.
        eventBus.Publish(new TurnStartedEvent(bob, 2));

        CastingRestrictions.CannotCastAnySpell(bob).Should().BeFalse(
            "the restriction only applies on the controller's turn");
    }

    // -----------------------------------------------------------------------
    // Mobilize 2: tapped + attacking tokens, sacrificed at next end step.
    // -----------------------------------------------------------------------

    [Fact]
    public void Mobilize_OnAttack_CreatesTwoTappedAndAttackingRedWarriorTokens()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);

        var voice = VoiceOfVictoryFactory.Create(
            alice,
            opponentResolver: null,
            eventBus: eventBus,
            triggers: triggers,
            combat: combat);
        alice.Zones.Battlefield.AddCard(voice);
        voice.SetZone(ZoneType.Battlefield);
        voice.ClearSummoningSickness();

        // Start combat and declare Voice of Victory as an attacker against Bob.
        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(voice, targetPlayer: bob),
        });

        // The CombatManager publishes AttackersDeclaredEvent on declaration,
        // but the Mobilize trigger fires on the per-attacker CreatureAttacksEvent.
        eventBus.Publish(new CreatureAttacksEvent(voice, bob));

        // Resolve the Mobilize trigger.
        triggers.PutPendingTriggersOnStack(alice);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (item is TriggeredAbility ta)
            {
                foreach (var eff in ta.Effects) eff.Execute();
            }
        }

        // Two 1/1 red Warrior tokens on the battlefield.
        var warriors = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Warrior))
            .ToList();

        warriors.Should().HaveCount(2, "Mobilize 2 creates two Warrior tokens");
        warriors.Should().AllSatisfy(w =>
        {
            w.BasePower.Should().Be(1);
            w.BaseToughness.Should().Be(1);
            CardColors.GetColors(w).Should().Contain(ManaColor.Red, "red Warriors");
            w.IsTapped.Should().BeTrue("tokens enter tapped");
        });

        // Both tokens are in the current combat's attacker set against Bob.
        var attackingCreatures = combat.CurrentCombat!.Attackers
            .Select(a => a.Creature)
            .ToList();
        foreach (var w in warriors)
        {
            attackingCreatures.Should().Contain(w, "tokens enter attacking");
        }
        combat.CurrentCombat.Attackers
            .Where(a => warriors.Contains(a.Creature))
            .Should().AllSatisfy(a =>
                a.TargetPlayer.Should().BeSameAs(bob,
                    "tokens attack the same defender as Voice of Victory"));
    }

    [Fact]
    public void Mobilize_Tokens_AreSacrificed_AtNextEndStep()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);

        var voice = VoiceOfVictoryFactory.Create(
            alice,
            opponentResolver: null,
            eventBus: eventBus,
            triggers: triggers,
            combat: combat);
        alice.Zones.Battlefield.AddCard(voice);
        voice.SetZone(ZoneType.Battlefield);
        voice.ClearSummoningSickness();

        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(voice, targetPlayer: bob),
        });
        eventBus.Publish(new CreatureAttacksEvent(voice, bob));

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
        warriors.Should().HaveCount(2);

        // Fire the end step — the delayed sacrifice trigger should resolve.
        eventBus.Publish(new StepStartedEvent(PhaseStateType.End, alice));
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
        remaining.Should().BeEmpty("the tokens are sacrificed at the next end step");
    }
}
