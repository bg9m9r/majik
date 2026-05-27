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
/// Unit tests for <see cref="HelpfulHunterFactory"/> ({1}{W}).
///
/// Creature — Cat 1/1. Oracle text:
///   "When this creature enters, draw a card."
///
/// Covers:
/// - Identity (Creature — Cat 1/1 at {1}{W}, white, mana value 2, no Flying).
/// - NamedCardFactory dispatch.
/// - ETB trigger wired via TriggerManager draws 1 card for controller.
/// </summary>
public class HelpfulHunterFactoryTests
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
    public void HelpfulHunter_Identity_Cat_1_1()
    {
        var card = HelpfulHunterFactory.Create(_alice);

        card.Name.Should().Be("Helpful Hunter");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HelpfulHunter_ManaValue_Is2()
    {
        var card = HelpfulHunterFactory.Create(_alice);

        Majik.Core.ValueObjects.ManaCost.Parse(card.ManaCost!).TotalValue.Should().Be(2);
    }

    [Fact]
    public void HelpfulHunter_IsWhite()
    {
        var card = HelpfulHunterFactory.Create(_alice);

        var colors = Majik.Core.Cards.CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.White, "mana cost {1}{W} — CR 105");
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Red);
        colors.Should().NotContain(ManaColor.Green);
    }

    [Fact]
    public void HelpfulHunter_NoFlying()
    {
        var card = HelpfulHunterFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Any(a => a.Keyword.Equals("Flying", StringComparison.OrdinalIgnoreCase))
            .Should().BeFalse();
    }

    // -------------------------------------------------------------------
    // Dispatch
    // -------------------------------------------------------------------

    [Fact]
    public void HelpfulHunter_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Helpful Hunter", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Helpful Hunter");
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
    }

    // -------------------------------------------------------------------
    // ETB trigger — draw a card
    // -------------------------------------------------------------------

    [Fact]
    public void HelpfulHunter_Etb_DrawsACard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = HelpfulHunterFactory.Create(_alice, eventBus: bus, triggers: triggers);

        var top = NewCardInLibrary(_alice, "TopCard");

        // Simulate Helpful Hunter entering the battlefield.
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
