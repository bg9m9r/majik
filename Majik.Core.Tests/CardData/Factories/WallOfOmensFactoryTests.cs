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
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WallOfOmensFactory"/>.
///
/// Card: Wall of Omens — Creature — Wall {1}{W} 0/4 (Rise of the Eldrazi).
/// Oracle text:
///   "Defender.
///    When this creature enters, draw a card."
///
/// Covers:
/// - Identity ({1}{W}, white, 0/4, Creature — Wall).
/// - Defender keyword marker (CR 702.3).
/// - NamedCardFactory dispatch.
/// - Exactly one battlefield-active ETB TriggeredAbility.
/// - ETB effect draws 1 card for the controller from a stocked library.
/// - ETB effect stamps the empty-library SBA flag (CR 704.5b) on shortage.
/// </summary>
public class WallOfOmensFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfOmens_Identity()
    {
        var c = WallOfOmensFactory.Create(_alice);

        c.Name.Should().Be("Wall of Omens");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wall).Should().BeTrue();
        c.BasePower.Should().Be(0);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Defender keyword
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfOmens_HasDefenderKeyword()
    {
        var c = WallOfOmensFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();

        keywords.Should().Contain("Defender",
            "CR 702.3 — Defender is a printed keyword ability on Wall of Omens");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfOmens_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Wall of Omens", _alice);

        c.Should().BeOfType<Creature>("Wall of Omens is a Creature");
        c.Name.Should().Be("Wall of Omens");
        c.HasSubtype(CardSubtype.Wall).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{W}");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfOmens_ExactlyOneBattlefieldActiveEtbTrigger()
    {
        var c = WallOfOmensFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();

        triggers.Should().HaveCount(1,
            "Wall of Omens has exactly one triggered ability — the ETB draw");

        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are active while the permanent is on the battlefield (CR 603.6a)");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — draw 1
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfOmens_EtbTrigger_DrawsOneCard()
    {
        var alice = new Player("Alice", 20);

        // Seed library with two known cards.
        var c1 = new Card("Top1", "");
        var c2 = new Card("Top2", "");
        foreach (var card in new[] { c1, c2 })
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        var wall = WallOfOmensFactory.Create(alice);
        var etb = wall.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "ETB draw draws exactly 1 card (CR 121.1)");
        alice.Zones.Library.GetCards().Should().HaveCount(1,
            "one card moved from library to hand");
        alice.Zones.Hand.GetCards().Should().Contain(c1,
            "the top card of the library is drawn");
    }

    [Fact]
    public void WallOfOmens_EtbTrigger_EmptyLibrary_StampsLossFlag_NoCrash()
    {
        var alice = new Player("Alice", 20);
        // Library is intentionally empty.

        var wall = WallOfOmensFactory.Create(alice);
        var etb = wall.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no cards in library → no draw (CR 704.5b loss flag is stamped)");
        alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "CR 704.5b — drawing from an empty library stamps the loss flag");
    }
}
