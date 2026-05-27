using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ElvishVisionaryFactory"/>.
///
/// Covers:
/// - Card identity: name, mana cost {1}{G}, P/T 1/1, subtypes Elf + Shaman,
///   card type Creature, mana value 2, owner/controller (CR 202, CR 205).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Single ETB <see cref="TriggeredAbility"/> active on the battlefield
///   (CR 603.6a).
/// - ETB trigger draws the top card from the controller's library into hand
///   (CR 121.1).
/// - ETB trigger with an empty library stamps the SBA loss flag without
///   crashing (CR 704.5b).
/// </summary>
public class ElvishVisionaryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ElvishVisionary_Identity()
    {
        var c = ElvishVisionaryFactory.Create(_alice);

        c.Name.Should().Be("Elvish Visionary");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue("Elvish Visionary is an Elf");
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue("Elvish Visionary is a Shaman");
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.ManaCostValue.TotalValue.Should().Be(2, "1 generic + 1 green = CMC 2 (CR 202.3)");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ElvishVisionary_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Elvish Visionary", _alice);

        c.Should().BeOfType<Creature>("Elvish Visionary is a Creature");
        c.Name.Should().Be("Elvish Visionary");
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{G}");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ElvishVisionary_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var c = ElvishVisionaryFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().ContainSingle("only one ETB trigger");
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB trigger is only active while on the battlefield (CR 603.6a)");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — draws 1 card
    // -----------------------------------------------------------------------

    [Fact]
    public void ElvishVisionary_EtbTrigger_DrawsTopCard_IntoHand()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var visionary = ElvishVisionaryFactory.Create(_alice, bus, triggers);
        visionary.SetZone(ZoneType.Battlefield);

        // Seed library with two cards.
        var top = new Card("Top", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var second = new Card("Second", "");
        second.SetOwner(_alice);
        _alice.Zones.Library.AddCard(second);
        second.SetZone(ZoneType.Library);

        // Publish ETB event — trigger queues.
        bus.Publish(new CardMovedEvent(visionary, ZoneType.Library, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1, "ETB trigger is pending");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top, "top of library moved to hand (CR 121.1)");
        _alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(second, "second card stays in library");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — empty library
    // -----------------------------------------------------------------------

    [Fact]
    public void ElvishVisionary_EtbTrigger_EmptyLibrary_StampsLossFlag_NoCrash()
    {
        var visionary = ElvishVisionaryFactory.Create(_alice);

        var etb = visionary.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no cards in library → nothing drawn");
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "CR 704.5b — drawing from empty library stamps the SBA loss flag");
    }
}
