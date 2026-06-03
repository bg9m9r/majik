using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Prompto Argentum (Final Fantasy, {1}{R}, Legendary Creature —
/// Human Scout 2/2).
///
/// Covers:
///   - Card identity (name, type, supertype, subtypes, P/T, mana cost).
///   - Haste keyword marker attached; exactly one cast trigger.
///   - Casting a noncreature spell with ≥4 mana spent → a Treasure is created.
///   - Casting a noncreature spell with &lt;4 mana spent → no trigger / no Treasure.
///   - Casting a creature spell (any cost) → no trigger.
///   - The intervening-if reads the watched spell's TotalManaSpentThisCast
///     (CR 118.10) — the total-amount mana-spent sentinel this PR adds.
/// </summary>
[Trait("Color", "R")]
public class PromptoArgentumFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Majik.Core.Spells.Spell NoncreatureSpell(
        Player controller, int totalManaSpent, string name = "Spell")
    {
        var instant = new Instant(name, "2R") { Owner = controller };
        var spell = new Majik.Core.Spells.Spell(instant, controller)
        {
            TotalManaSpentThisCast = totalManaSpent,
        };
        return spell;
    }

    private static Majik.Core.Spells.Spell CreatureSpell(
        Player controller, int totalManaSpent, string name = "Bear")
    {
        var creature = new Creature(name, "3GG", 4, 4) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller)
        {
            TotalManaSpentThisCast = totalManaSpent,
        };
    }

    private static int TreasuresControlled(Player p) =>
        p.Zones.Battlefield.GetCards().Count(c => c.Name == "Treasure");

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void PromptoArgentum_Identity_LegendaryHumanScout_2_2_At1R()
    {
        var card = PromptoArgentumFactory.Create(_alice);

        card.Name.Should().Be("Prompto Argentum");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PromptoArgentum_HasHasteKeyword_AndOneCastTrigger()
    {
        var card = PromptoArgentumFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Haste");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Selfie Shot — total-mana-spent intervening-if
    // -----------------------------------------------------------------------

    [Fact]
    public void NoncreatureSpell_FourManaSpent_CreatesTreasure()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var card = PromptoArgentumFactory.Create(_alice, triggers, zones);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NoncreatureSpell(_alice, totalManaSpent: 4)));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        TreasuresControlled(_alice).Should().Be(1);
    }

    [Fact]
    public void NoncreatureSpell_ThreeManaSpent_NoTrigger_NoTreasure()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var card = PromptoArgentumFactory.Create(_alice, triggers, zones);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NoncreatureSpell(_alice, totalManaSpent: 3)));

        // Intervening-if not met (< 4 mana spent) — the ability never queues.
        triggers.PendingCount.Should().Be(0);
        TreasuresControlled(_alice).Should().Be(0);
    }

    [Fact]
    public void NoncreatureSpell_ExactlyThreshold_Boundary()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var card = PromptoArgentumFactory.Create(_alice, triggers, zones);
        card.SetZone(ZoneType.Battlefield);

        // Exactly 4 = "at least four" — fires.
        bus.Publish(new SpellCastEvent(NoncreatureSpell(_alice, totalManaSpent: 4)));
        triggers.PendingCount.Should().Be(1);
    }

    [Fact]
    public void CreatureSpell_EvenWithFourMana_NoTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var card = PromptoArgentumFactory.Create(_alice, triggers, zones);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(CreatureSpell(_alice, totalManaSpent: 5)));

        triggers.PendingCount.Should().Be(0);
        TreasuresControlled(_alice).Should().Be(0);
    }

    [Fact]
    public void OpponentNoncreatureSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);
        var bob = new Player("Bob", 20);

        var card = PromptoArgentumFactory.Create(_alice, triggers, zones);
        card.SetZone(ZoneType.Battlefield);

        // "Whenever YOU cast a noncreature spell" — opponent casts don't count.
        bus.Publish(new SpellCastEvent(NoncreatureSpell(bob, totalManaSpent: 6)));

        triggers.PendingCount.Should().Be(0);
        TreasuresControlled(_alice).Should().Be(0);
    }
}
