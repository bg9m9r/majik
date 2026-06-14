using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FearOfTheDarkFactory"/>.
///
/// Fear of the Dark — {4}{B} Enchantment Creature — Nightmare 5/5:
///   "Whenever this creature attacks, if defending player controls no Glimmer
///    creatures, it gains menace and deathtouch until end of turn."
/// </summary>
[Trait("Color", "B")]
public class FearOfTheDarkFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void FearOfTheDark_IsBlackNightmareEnchantmentCreature_5_5()
    {
        var card = FearOfTheDarkFactory.Create(_alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Fear of the Dark");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Enchantment).Should().BeTrue("Enchantment Creature (CR 301.1)");
        card.HasSubtype(CardSubtype.Nightmare).Should().BeTrue();
        card.BasePower.Should().Be(5);
        card.BaseToughness.Should().Be(5);
        card.ManaCostValue.TotalValue.Should().Be(5, "{4}{B} is mana value 5");
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
    }

    [Fact]
    public void Attack_DefenderHasNoGlimmer_GrantsMenaceAndDeathtouchEOT()
    {
        var svc = new ContinuousEffectsService();
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = FearOfTheDarkFactory.Create(_alice, svc, triggers);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.ClearSummoningSickness();

        // Defender controls a non-Glimmer creature only.
        var bob = new Player("Bob", 20);
        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        svc.Compute(card).Keywords.Should().NotContain("Menace");
        svc.Compute(card).Keywords.Should().NotContain("Deathtouch");

        bus.Publish(new CreatureAttacksEvent(card, bob));
        triggers.PendingCount.Should().Be(1, "no Glimmer → intervening-if holds (CR 603.4)");

        ResolveTriggers(triggers, stack, _alice);

        svc.Compute(card).Keywords.Should().Contain("Menace", "CR 702.111");
        svc.Compute(card).Keywords.Should().Contain("Deathtouch", "CR 702.2");
    }

    [Fact]
    public void Attack_DefenderControlsGlimmer_DoesNotFire()
    {
        var svc = new ContinuousEffectsService();
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = FearOfTheDarkFactory.Create(_alice, svc, triggers);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.ClearSummoningSickness();

        // Defender controls a Glimmer creature → intervening-if fails.
        var bob = new Player("Bob", 20);
        var glimmer = new Creature("Enduring Curiosity", "{2}{U}{U}", 3, 3,
            subtypes: new[] { CardSubtype.Cat, CardSubtype.Glimmer }) { Owner = bob, Controller = bob };
        bob.Zones.Battlefield.AddCard(glimmer);
        glimmer.SetZone(ZoneType.Battlefield);

        bus.Publish(new CreatureAttacksEvent(card, bob));

        triggers.PendingCount.Should().Be(0, "defender controls a Glimmer creature (CR 603.4)");
        svc.Compute(card).Keywords.Should().NotContain("Menace");
        svc.Compute(card).Keywords.Should().NotContain("Deathtouch");
    }

    [Fact]
    public void DefendingPlayerControlsNoGlimmer_NullDefender_IsTrue()
    {
        // Attacking a planeswalker (no defending Player) is treated as "no
        // Glimmer creatures" so the grant lands.
        FearOfTheDarkFactory.DefendingPlayerControlsNoGlimmer(null).Should().BeTrue();
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
