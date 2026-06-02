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
/// Unit tests for <see cref="LlanowarVisionaryFactory"/>.
///
/// Llanowar Visionary (Dominaria, {2}{G}) — Creature — Elf Druid 2/2.
/// Oracle text:
///   "When this creature enters, draw a card."
///   "{T}: Add {G}."
///
/// Covers:
/// - Card identity: name, mana cost {2}{G}, P/T 2/2, subtypes Elf + Druid,
///   card type Creature, mana value 3, owner/controller (CR 202, CR 205).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Single ETB <see cref="TriggeredAbility"/> active on the battlefield
///   (CR 603.6a) that draws the top card into hand (CR 121.1).
/// - ETB trigger with an empty library stamps the SBA loss flag without
///   crashing (CR 704.5b).
/// - One <see cref="ManaAbility"/> that produces {G} (CR 605.1).
/// </summary>
[Trait("Color", "G")]
public class LlanowarVisionaryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void LlanowarVisionary_Identity()
    {
        var c = LlanowarVisionaryFactory.Create(_alice);

        c.Name.Should().Be("Llanowar Visionary");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue("Llanowar Visionary is an Elf");
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue("Llanowar Visionary is a Druid");
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.ManaCostValue.TotalValue.Should().Be(3, "2 generic + 1 green = CMC 3 (CR 202.3)");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // ETB triggered ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void LlanowarVisionary_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var c = LlanowarVisionaryFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().ContainSingle("only one ETB trigger");
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB trigger is only active while on the battlefield (CR 603.6a)");
    }

    // -----------------------------------------------------------------------
    // Mana ability — {T}: Add {G}
    // -----------------------------------------------------------------------

    [Fact]
    public void LlanowarVisionary_HasExactlyOneManaAbility_ProducingGreen()
    {
        var c = LlanowarVisionaryFactory.Create(_alice);

        var mana = c.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().ContainSingle("Llanowar Visionary has one mana ability: {T}: Add {G} (CR 605.1)");
        mana[0].ManaGenerated.ToString().Should().Be("G",
            "the mana ability produces a single green mana");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — draws 1 card
    // -----------------------------------------------------------------------

    [Fact]
    public void LlanowarVisionary_EtbTrigger_DrawsTopCard_IntoHand()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var visionary = LlanowarVisionaryFactory.Create(_alice, bus, triggers);
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
    public void LlanowarVisionary_EtbTrigger_EmptyLibrary_StampsLossFlag_NoCrash()
    {
        var visionary = LlanowarVisionaryFactory.Create(_alice);

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
