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
/// Tests for <see cref="HonoredCropCaptainFactory"/>.
///
/// Honored Crop-Captain — {R}{W} Creature — Human Warrior, 3/2:
///   "Whenever this creature attacks, other attacking creatures get +1/+0
///    until end of turn."
///
/// Covers:
/// - Identity: {R}{W} 3/2 red-white Human Warrior, mana value 2, dispatch.
/// - Attack trigger: on attack, each OTHER attacking creature gets +1/+0 EOT;
///   Honored Crop-Captain itself is not pumped.
/// - The card has NO "Battle cry" keyword marker (the printed text is written
///   out, not the keyword).
/// </summary>
[Trait("Color", "RW")]
public class HonoredCropCaptainFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void HonoredCropCaptain_IsRedWhiteHumanWarrior_3_2_ManaValue2()
    {
        var alice = new Player("Alice", 20);
        var card = HonoredCropCaptainFactory.Create(alice);

        card.Should().BeOfType<Creature>();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Name.Should().Be("Honored Crop-Captain");
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(2);
        card.ManaCostValue.TotalValue.Should().Be(2, "{R}{W} is mana value 2");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Red);
        colors.Should().Contain(ManaColor.White);
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void HonoredCropCaptain_HasNoBattleCryKeywordMarker()
    {
        var alice = new Player("Alice", 20);
        var card = HonoredCropCaptainFactory.Create(alice);

        // The printed text is written out, not the "Battle cry" keyword.
        card.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Battle cry", StringComparison.OrdinalIgnoreCase))
            .Should().BeFalse("Honored Crop-Captain spells the ability out — it is not the Battle cry keyword");
    }

    // -----------------------------------------------------------------------
    // Attack trigger: each OTHER attacking creature gets +1/+0 EOT.
    // -----------------------------------------------------------------------

    [Fact]
    public void OnAttack_PumpsEachOtherAttackingCreature_NotItself()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);
        var effects = new ContinuousEffectsService();

        var captain = HonoredCropCaptainFactory.Create(
            alice,
            triggers: triggers,
            attackingCreaturesSource: () => combat.CurrentCombat?.Attackers
                .Select(a => a.Creature).ToList() ?? new System.Collections.Generic.List<Creature>());
        captain.ActiveEffects = effects;
        alice.Zones.Battlefield.AddCard(captain);
        captain.SetZone(ZoneType.Battlefield);
        captain.ClearSummoningSickness();

        // A second attacker that should be pumped.
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
            new AttackerDeclaration(captain, targetPlayer: bob),
            new AttackerDeclaration(ally, targetPlayer: bob),
        });
        eventBus.Publish(new CreatureAttacksEvent(captain, bob));

        ResolveTriggers(triggers, stack, alice);

        // The other attacker is pumped +1/+0.
        ally.GetPower().Should().Be(3, "other attacking creatures get +1/+0");
        ally.GetToughness().Should().Be(2, "the pump is +1/+0 — toughness unchanged");

        // Honored Crop-Captain itself is NOT pumped ("other attacking creatures").
        captain.GetPower().Should().Be(3, "Crop-Captain is not pumped by its own trigger");
        captain.GetToughness().Should().Be(2);
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
