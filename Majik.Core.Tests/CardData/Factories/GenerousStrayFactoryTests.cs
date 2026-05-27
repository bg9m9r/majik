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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GenerousStrayFactory"/>.
///
/// Covers:
///   - Identity ({2}{G} 1/2 green Cat, mana value 3, owner/controller).
///   - No Flying keyword ability (Generous Stray has no evasion).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - ETB triggered ability is present (shape overload).
///   - WIRED Create with TriggerManager — entering battlefield draws 1 card
///     for the controller (CR 603.3 / 603.6a).
///   - Empty-library draw stamps the SBA loss flag (CR 704.5b) without crashing.
/// </summary>
public class GenerousStrayFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerousStray_Identity()
    {
        var c = GenerousStrayFactory.Create(_alice);

        c.Name.Should().Be("Generous Stray");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.ManaCost.Should().Be("{2}{G}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GenerousStray_ManaValue_IsThree()
    {
        var c = GenerousStrayFactory.Create(_alice);

        // {2}{G} → converted mana cost / mana value = 3 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(3);
    }

    [Fact]
    public void GenerousStray_HasNoFlyingAbility()
    {
        var c = GenerousStrayFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().NotContain(k => k.Keyword == "Flying",
                "Generous Stray has no evasion keywords");
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerousStray_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Generous Stray", _alice);

        c.Should().BeOfType<Creature>("Generous Stray is a Creature");
        c.Name.Should().Be("Generous Stray");
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.ManaCost.Should().Be("{2}{G}");
    }

    // -----------------------------------------------------------------------
    // ETB trigger shape (shape-only Create overload)
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerousStray_HasOneEtbTriggeredAbility()
    {
        var c = GenerousStrayFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "exactly the ETB draw-a-card trigger");
    }

    // -----------------------------------------------------------------------
    // ETB trigger fires — wired Create overload
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerousStray_EtbTrigger_DrawsOneCard_ForController()
    {
        var alice = new Player("Alice", 20);

        // Seed library with three known cards.
        var c1 = new Card("Top1", "");
        var c2 = new Card("Top2", "");
        var c3 = new Card("Top3", "");
        foreach (var card in new[] { c1, c2, c3 })
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var stray = GenerousStrayFactory.Create(alice, bus, triggers);

        // Simulate entering the battlefield by publishing CardMovedEvent.
        stray.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(stray);
        bus.Publish(new CardMovedEvent(stray, ZoneType.Library, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1, "ETB trigger should be pending");
        triggers.PutPendingTriggersOnStack(alice);
        stack.Pop()!.Resolve();

        alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "Generous Stray ETB draws exactly one card (CR 603.6a)");
        alice.Zones.Library.GetCards().Should().HaveCount(2,
            "one card left the top of the library");
    }

    [Fact]
    public void GenerousStray_EtbTrigger_EmptyLibrary_StampsLossFlag_NoCrash()
    {
        var alice = new Player("Alice", 20);
        // Library is empty.

        var stray = GenerousStrayFactory.Create(alice);

        var etbTrigger = stray.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var effect in etbTrigger.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no cards in library → no draws (CR 704.5b loss flag is stamped)");
        alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "CR 704.5b — drawing from an empty library stamps the loss flag");
    }
}
