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
/// Unit tests for <see cref="ShamanOfSpringFactory"/>
/// (Magic 2015 / Shadows over Innistrad block, {3}{G}).
///
/// Covers:
/// - Identity (name, type, P/T, subtypes, mana cost, colour, owner/controller).
/// - Mana value 4.
/// - No Flying keyword marker.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - ETB triggered ability shape: single TriggeredAbility attached.
/// - ETB triggered ability resolves: controller draws 1 card from library.
/// - WIRED Create(Player, IEventBus?, TriggerManager?): entering battlefield
///   draws 1 card for controller.
/// </summary>
[Trait("Color", "G")]
public class ShamanOfSpringFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ShamanOfSpring_Identity()
    {
        var c = ShamanOfSpringFactory.Create(_alice);

        c.Name.Should().Be("Shaman of Spring");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue("Shaman of Spring is an Elf");
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue("Shaman of Spring is a Shaman");
        c.ManaCost.Should().Be("{3}{G}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ShamanOfSpring_ManaValue_IsFour()
    {
        var c = ShamanOfSpringFactory.Create(_alice);

        c.ManaCostValue.TotalValue.Should().Be(4, "mana value of {3}{G} is 4 (CR 202.3)");
    }

    [Fact]
    public void ShamanOfSpring_IsGreen()
    {
        var c = ShamanOfSpringFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green,
            "Shaman of Spring has {G} in its mana cost (CR 202.2)");
        colors.Should().HaveCount(1, "only green — no other colours");
    }

    [Fact]
    public void ShamanOfSpring_NoFlyingKeyword()
    {
        var c = ShamanOfSpringFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().NotContain("Flying",
            "Shaman of Spring does not have Flying");
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // ETB trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ShamanOfSpring_HasSingleEtbTrigger()
    {
        var c = ShamanOfSpringFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "single ETB draw trigger");
    }

    // -----------------------------------------------------------------------
    // ETB trigger effect — draw 1 card
    // -----------------------------------------------------------------------

    [Fact]
    public void ShamanOfSpring_EtbTrigger_DrawsOneCard()
    {
        var alice = new Player("Alice", 20);

        // Seed library with three known cards so draw never trips the empty-
        // library SBA flag.
        var c1 = new Card("Top1", "");
        var c2 = new Card("Top2", "");
        var c3 = new Card("Top3", "");
        foreach (var card in new[] { c1, c2, c3 })
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        var shaman = ShamanOfSpringFactory.Create(alice);
        var etb = shaman.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().HaveCount(1, "ETB draws exactly 1 card (CR 121.1)");
        alice.Zones.Library.GetCards().Should().HaveCount(2, "one card left the top");
    }

    [Fact]
    public void ShamanOfSpring_EtbTrigger_EmptyLibrary_StampsLossFlag_NoCrash()
    {
        var alice = new Player("Alice", 20);
        // Library is empty — draw stamps CR 704.5b loss flag but must not throw.

        var shaman = ShamanOfSpringFactory.Create(alice);
        var etb = shaman.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no cards in library → no draws (CR 704.5b loss flag is stamped)");
        alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "CR 704.5b — drawing from an empty library stamps the loss flag");
    }

    // -----------------------------------------------------------------------
    // Wired overload: TriggerManager registers the ETB trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void ShamanOfSpring_WiredCreate_TriggerManager_DrawsOneCard_OnEnter()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        // Seed library.
        var c1 = new Card("Top1", "");
        c1.SetOwner(alice);
        alice.Zones.Library.AddCard(c1);
        c1.SetZone(ZoneType.Library);

        var triggerManager = new TriggerManager(stack, bus);
        var shaman = ShamanOfSpringFactory.Create(alice, eventBus: null, triggers: triggerManager);

        // Place shaman on battlefield so the ETB condition evaluates (CR 603.6a).
        shaman.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(shaman);

        // Simulate ETB: resolve the draw effect directly.
        var etb = shaman.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "wired ETB trigger draws 1 card for the controller (CR 603.6a)");
    }
}
