using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Up the Beanstalk (Wilds of Eldraine — Enchanting Tales, {1}{G}).
///
/// Covers:
///   - Card identity (name, type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch hands back the same shape.
///   - Two triggered abilities surfaced on the card.
///   - Mechanic: ETB → controller draws a card (top of library → hand).
///   - Mechanic: controller casting a spell with mana value 4 does NOT
///     trigger.
///   - Mechanic: controller casting a spell with mana value 5 triggers
///     one draw.
///   - Mechanic: controller casting a spell with mana value 7 triggers
///     one draw.
///   - Mechanic: opponent casting a mana-value-5 spell does NOT trigger
///     (controller-only gating).
/// </summary>
public class UpTheBeanstalkTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Card NewCardInLibrary(Player owner, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    /// <summary>
    /// Build a synthetic <see cref="Spell"/> whose printed mana cost has
    /// the given <paramref name="manaCost"/> string (e.g. <c>"4G"</c> for
    /// mana value 5). The factory's cast trigger reads
    /// <c>ISpell.Card.ManaCostValue.TotalValue</c>.
    /// </summary>
    private static Majik.Core.Spells.Spell NewSpell(Player controller, string manaCost, string name = "TestSpell")
    {
        var instant = new Instant(name, manaCost) { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    [Fact]
    public void UpTheBeanstalk_Identity_EnchantmentAt1G()
    {
        var utb = UpTheBeanstalkFactory.Create(_alice);

        utb.Name.Should().Be("Up the Beanstalk");
        utb.ManaCost.Should().Be("{1}{G}");
        utb.HasType(CardType.Enchantment).Should().BeTrue();
        utb.Owner.Should().BeSameAs(_alice);
        utb.Controller.Should().BeSameAs(_alice);
        utb.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void UpTheBeanstalk_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Up the Beanstalk", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Up the Beanstalk");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void Etb_DrawsTopOfLibrary_IntoControllersHand()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var utb = UpTheBeanstalkFactory.Create(_alice, triggers);
        utb.SetZone(ZoneType.Battlefield);

        var top = NewCardInLibrary(_alice, "Top");

        // Simulate the aura entering the battlefield.
        bus.Publish(new CardMovedEvent(utb, ZoneType.Library, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { top });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void CastSpell_ManaValue4_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var utb = UpTheBeanstalkFactory.Create(_alice, triggers);
        utb.SetZone(ZoneType.Battlefield);

        NewCardInLibrary(_alice, "Top");

        // "3G" → generic 3 + green 1 = 4. Below the 5+ threshold.
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "3G", "MV4")));

        triggers.PendingCount.Should().Be(0);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void CastSpell_ManaValue5_TriggersDrawOne()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var utb = UpTheBeanstalkFactory.Create(_alice, triggers);
        utb.SetZone(ZoneType.Battlefield);

        var top = NewCardInLibrary(_alice, "Top");

        // "4G" → 4 generic + 1 green = mana value 5.
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "4G", "MV5")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { top });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void CastSpell_ManaValue7_TriggersDrawOne()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var utb = UpTheBeanstalkFactory.Create(_alice, triggers);
        utb.SetZone(ZoneType.Battlefield);

        var top = NewCardInLibrary(_alice, "Top");

        // "5GG" → 5 generic + 2 green = mana value 7.
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "5GG", "MV7")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { top });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void OpponentCastsManaValue5_DoesNotTrigger_ControllerOnly()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var utb = UpTheBeanstalkFactory.Create(_alice, triggers);
        utb.SetZone(ZoneType.Battlefield);

        NewCardInLibrary(_alice, "AliceTop");

        // Bob (opponent) casts a mana-value-5 spell — Alice's Beanstalk
        // must not trigger. CR 603.2 + oracle "Whenever you cast …".
        bus.Publish(new SpellCastEvent(NewSpell(_bob, "4G", "BobMV5")));

        triggers.PendingCount.Should().Be(0);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
