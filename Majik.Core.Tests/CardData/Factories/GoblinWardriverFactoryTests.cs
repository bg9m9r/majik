using System;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for <see cref="GoblinWardriverFactory"/>.
///
/// Goblin Wardriver — {R}{R} Creature — Goblin Warrior, 2/2:
///   "Battle cry (Whenever this creature attacks, each other attacking
///    creature gets +1/+0 until end of turn.)"
///
/// Covers:
/// - Identity: {R}{R} 2/2 red Goblin Warrior, mana value 2, dispatch shape.
/// - Battle-cry keyword marker on ICard.Abilities.
/// - Battle cry: on attack, each OTHER attacking creature gets +1/+0 EOT;
///   Goblin Wardriver itself is not pumped (CR 702.92a — "each other").
/// </summary>
[Trait("Color", "R")]
public class GoblinWardriverFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinWardriver_IsRedGoblinWarrior_2_2_ManaValue2()
    {
        var alice = new Player("Alice", 20);
        var card = GoblinWardriverFactory.Create(alice);

        card.Should().BeOfType<Creature>();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Name.Should().Be("Goblin Wardriver");
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.ManaCostValue.TotalValue.Should().Be(2, "{R}{R} is mana value 2");
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void GoblinWardriver_HasBattleCryKeywordMarker()
    {
        var alice = new Player("Alice", 20);
        var card = GoblinWardriverFactory.Create(alice);

        card.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Battle cry", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("the printed line is Battle cry");
    }

    // -----------------------------------------------------------------------
    // Battle cry: each OTHER attacking creature gets +1/+0 EOT.
    // -----------------------------------------------------------------------

    [Fact]
    public void BattleCry_PumpsEachOtherAttackingCreature_NotWardriverItself()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);
        var effects = new ContinuousEffectsService();

        var wardriver = GoblinWardriverFactory.Create(
            alice,
            triggers: triggers,
            attackingCreaturesSource: () => combat.CurrentCombat?.Attackers
                .Select(a => a.Creature).ToList() ?? new System.Collections.Generic.List<Creature>());
        wardriver.ActiveEffects = effects;
        alice.Zones.Battlefield.AddCard(wardriver);
        wardriver.SetZone(ZoneType.Battlefield);
        wardriver.ClearSummoningSickness();

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
            new AttackerDeclaration(wardriver, targetPlayer: bob),
            new AttackerDeclaration(ally, targetPlayer: bob),
        });
        eventBus.Publish(new CreatureAttacksEvent(wardriver, bob));

        ResolveTriggers(triggers, stack, alice);

        // The other attacker is pumped +1/+0.
        ally.GetPower().Should().Be(3, "battle cry gives each other attacker +1/+0");
        ally.GetToughness().Should().Be(2, "battle cry is +1/+0 — toughness unchanged");

        // Goblin Wardriver itself is NOT pumped by its own battle cry.
        wardriver.GetPower().Should().Be(2, "Wardriver is not pumped by its own battle cry");
        wardriver.GetToughness().Should().Be(2);
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
