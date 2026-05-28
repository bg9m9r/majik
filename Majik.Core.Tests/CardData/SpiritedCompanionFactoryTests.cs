using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SpiritedCompanionFactory"/> ({1}{W}).
///
/// Enchantment Creature — Dog 1/1. Oracle text:
///   "When this creature enters, draw a card."
///
/// Covers:
/// - Identity (Creature — Dog 1/1 at {1}{W}, white, mana value 2, no Flying).
/// - NamedCardFactory dispatch.
/// - ETB trigger wired via TriggerManager draws 1 card for controller.
/// </summary>
public class SpiritedCompanionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Card NewCardInLibrary(Player owner, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    // -------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------

    [Fact]
    public void SpiritedCompanion_Identity_Dog_1_1()
    {
        var card = SpiritedCompanionFactory.Create(_alice);

        card.Name.Should().Be("Spirited Companion");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Dog).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpiritedCompanion_ManaValue_Is2()
    {
        var card = SpiritedCompanionFactory.Create(_alice);

        Majik.Core.ValueObjects.ManaCost.Parse(card.ManaCost!).TotalValue.Should().Be(2);
    }

    [Fact]
    public void SpiritedCompanion_IsWhite()
    {
        var card = SpiritedCompanionFactory.Create(_alice);

        var colors = Majik.Core.Cards.CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.White, "mana cost {1}{W} — CR 105");
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Red);
        colors.Should().NotContain(ManaColor.Green);
    }

    [Fact]
    public void SpiritedCompanion_NoFlying()
    {
        var card = SpiritedCompanionFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Any(a => a.Keyword.Equals("Flying", StringComparison.OrdinalIgnoreCase))
            .Should().BeFalse();
    }

    // -------------------------------------------------------------------
    // Dispatch
    // -------------------------------------------------------------------

    [Fact]
    public void SpiritedCompanion_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Spirited Companion", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Spirited Companion");
        c.HasSubtype(CardSubtype.Dog).Should().BeTrue();
    }

    // -------------------------------------------------------------------
    // ETB trigger — draw a card
    // -------------------------------------------------------------------

    [Fact]
    public void SpiritedCompanion_Etb_DrawsACard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = SpiritedCompanionFactory.Create(_alice, eventBus: bus, triggers: triggers);

        var top = NewCardInLibrary(_alice, "TopCard");

        // Simulate Spirited Companion entering the battlefield.
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(card, ZoneType.Hand, ZoneType.Battlefield));

        // Exactly one trigger should be pending.
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top);
    }
}
