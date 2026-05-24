using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Silvergill Adept and Cursecatcher — both Merfolk Wizards.
///
/// Covers:
///   - Silvergill Adept: ETB triggers a draw (top of library → hand).
///   - Cursecatcher activated: target spell controller cannot pay {1}
///     → spell countered.
///   - Cursecatcher activated: target spell controller can pay {1}
///     → spell NOT countered.
///   - Both cards have CardSubtype.Merfolk and CardSubtype.Wizard.
/// </summary>
public class SilvergillAdeptTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static Card NewCardInLibrary(Player owner, string name = "LibraryCard")
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    /// <summary>
    /// Build a minimal <see cref="Instant"/> so Cursecatcher can target it.
    /// The card's owner and controller are both <paramref name="controller"/>.
    /// </summary>
    private static Instant NewInstant(Player controller, string manaCost = "{1}")
    {
        var card = new Instant("TestSpell", manaCost);
        card.SetOwner(controller);
        card.SetController(controller);
        return card;
    }

    // ----------------------------------------------------------------
    // Silvergill Adept — ETB draw
    // ----------------------------------------------------------------

    [Fact]
    public void SilvergillAdept_Etb_DrawsTopOfLibraryIntoHand()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var adept = SilvergillAdeptFactory.Create(_alice, bus, triggers);
        adept.SetZone(ZoneType.Battlefield);

        var top = NewCardInLibrary(_alice);

        // Simulate Silvergill Adept entering the battlefield.
        bus.Publish(new CardMovedEvent(adept, ZoneType.Library, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(top);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // Cursecatcher — counter unless pay {1}
    // ----------------------------------------------------------------

    [Fact]
    public void Cursecatcher_Activated_TargetCannotPay_SpellCountered()
    {
        var bus = new EventBus();
        var gameStack = new Majik.Core.Stack.Stack(bus);

        var cursecatcher = CursecatcherFactory.Create(_alice, gameStack);
        cursecatcher.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cursecatcher);

        // Bob controls the target spell; his mana pool is empty (cannot pay {1}).
        var targetInstant = NewInstant(_bob, "{1}{U}");
        var spell = new Majik.Core.Spells.Spell(targetInstant, _bob);
        gameStack.Push(spell);

        // Get the activated ability and set ChosenTargets to the target spell.
        var activatedAbility = cursecatcher.Abilities
            .OfType<ActivatedAbility>()
            .Single();

        activatedAbility.SetChosenTargets(new[] { new[] { (object)spell } });

        // Resolve the ability.
        activatedAbility.Resolve();

        // Spell should have been removed from the stack (countered).
        gameStack.GetAll().Should().NotContain(spell, "spell was countered");
        gameStack.IsEmpty.Should().BeTrue();

        // Cursecatcher should have been sacrificed to its owner's graveyard.
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().BeSameAs(cursecatcher);
    }

    [Fact]
    public void Cursecatcher_Activated_TargetPays_SpellNotCountered()
    {
        var bus = new EventBus();
        var gameStack = new Majik.Core.Stack.Stack(bus);

        var cursecatcher = CursecatcherFactory.Create(_alice, gameStack);
        cursecatcher.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cursecatcher);

        // Bob controls the target spell and has {1} generic mana to pay.
        var targetInstant = NewInstant(_bob, "{1}{U}");
        var spell = new Majik.Core.Spells.Spell(targetInstant, _bob);
        gameStack.Push(spell);

        // Give Bob {1} generic mana so he can pay.
        _bob.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("{1}"));

        var activatedAbility = cursecatcher.Abilities
            .OfType<ActivatedAbility>()
            .Single();

        activatedAbility.SetChosenTargets(new[] { new[] { (object)spell } });

        // Resolve the ability.
        activatedAbility.Resolve();

        // Spell should still be on the stack (was NOT countered).
        gameStack.GetAll().Should().Contain(spell, "controller paid {1} — spell survives");

        // Bob's mana pool should be drained (he paid {1}).
        _bob.ManaPool.Total.Should().Be(0, "Bob paid {1} to prevent the counter");
    }

    // ----------------------------------------------------------------
    // Subtype verification — both cards are Merfolk Wizards
    // ----------------------------------------------------------------

    [Fact]
    public void BothCards_HaveMerfolkAndWizardSubtypes()
    {
        var adept = SilvergillAdeptFactory.Create(_alice);
        var cursecatcher = CursecatcherFactory.Create(_alice);

        adept.HasSubtype(CardSubtype.Merfolk).Should().BeTrue("Silvergill Adept is a Merfolk");
        adept.HasSubtype(CardSubtype.Wizard).Should().BeTrue("Silvergill Adept is a Wizard");

        cursecatcher.HasSubtype(CardSubtype.Merfolk).Should().BeTrue("Cursecatcher is a Merfolk");
        cursecatcher.HasSubtype(CardSubtype.Wizard).Should().BeTrue("Cursecatcher is a Wizard");
    }
}
