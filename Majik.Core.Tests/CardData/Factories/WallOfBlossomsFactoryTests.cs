using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WallOfBlossomsFactory"/>.
///
/// Card: Wall of Blossoms — Creature — Plant Wall {1}{G} 0/4 (Tempest).
/// Oracle text (verified against Scryfall):
///   "Defender.
///    When this creature enters, draw a card."
///
/// Functionally identical to Wall of Omens; only colour/cost/subtype differ.
///
/// Covers:
/// - Identity ({1}{G}, green, 0/4, Creature — Plant Wall).
/// - Defender keyword marker (CR 702.3) + CombatAbilities.HasDefender.
/// - NamedCardFactory dispatch.
/// - Exactly one battlefield-active ETB TriggeredAbility.
/// - ETB effect draws 1 card for the controller from a stocked library.
/// - ETB effect stamps the empty-library SBA flag (CR 704.5b) on shortage.
/// </summary>
public class WallOfBlossomsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity ────────────────────────────────────────────────────────

    [Fact]
    public void WallOfBlossoms_Identity()
    {
        var c = WallOfBlossomsFactory.Create(_alice);

        c.Name.Should().Be("Wall of Blossoms");
        c.ManaCost.Should().Be("{1}{G}");
        c.ManaCostValue.TotalValue.Should().Be(2);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Plant).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wall).Should().BeTrue();
        c.BasePower.Should().Be(0);
        c.BaseToughness.Should().Be(4);
        CardColors.GetColors(c).Should().Contain(ManaColor.Green,
            "{1}{G} makes Wall of Blossoms a green card (CR 105.2a)");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // ── Defender keyword ────────────────────────────────────────────────

    [Fact]
    public void WallOfBlossoms_HasDefenderKeyword()
    {
        var c = WallOfBlossomsFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Defender",
                "CR 702.3 — Defender is a printed keyword ability on Wall of Blossoms");

        CombatAbilities.HasDefender(c).Should().BeTrue(
            "CR 702.3b — a creature with defender can't attack");
    }

    // ── NamedCardFactory dispatch ───────────────────────────────────────

    [Fact]
    public void WallOfBlossoms_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Wall of Blossoms", _alice);

        c.Should().BeOfType<Creature>("Wall of Blossoms is a Creature");
        c.Name.Should().Be("Wall of Blossoms");
        ((Creature)c).HasSubtype(CardSubtype.Wall).Should().BeTrue();
        ((Creature)c).HasSubtype(CardSubtype.Plant).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{G}");
    }

    // ── ETB triggered ability — shape ───────────────────────────────────

    [Fact]
    public void WallOfBlossoms_ExactlyOneBattlefieldActiveEtbTrigger()
    {
        var c = WallOfBlossomsFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();

        triggers.Should().HaveCount(1,
            "Wall of Blossoms has exactly one triggered ability — the ETB draw");

        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are active while the permanent is on the battlefield (CR 603.6a)");
    }

    // ── ETB triggered ability — draw 1 ──────────────────────────────────

    [Fact]
    public void WallOfBlossoms_EtbTrigger_DrawsOneCard()
    {
        var alice = new Player("Alice", 20);

        var c1 = new Card("Top1", "");
        var c2 = new Card("Top2", "");
        foreach (var card in new[] { c1, c2 })
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        var wall = WallOfBlossomsFactory.Create(alice);
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
    public void WallOfBlossoms_EtbTrigger_EmptyLibrary_StampsLossFlag_NoCrash()
    {
        var alice = new Player("Alice", 20);
        // Library is intentionally empty.

        var wall = WallOfBlossomsFactory.Create(alice);
        var etb = wall.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no cards in library → no draw");
        alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "CR 704.5b — drawing from an empty library stamps the loss flag");
    }
}
