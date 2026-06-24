using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="PatientNaturalistFactory"/> — Creature — Human Scout
/// {2}{G} 2/3 (Modern Horizons 3). Oracle text (verified against Scryfall
/// 2026-06-24):
///   "When this creature enters, mill three cards. Put a land card from among
///    the milled cards into your hand. If you can't, create a Treasure token.
///    (To mill three cards, put the top three cards of your library into your
///    graveyard.)"
///
/// Covers ONLY the card's unique behaviour (the dispatch + well-formedness is
/// covered by <c>CardFactoryContractTests</c>):
///   - Identity (Creature, Human Scout, 2/3, {2}{G}) — one assert.
///   - Single ETB triggered ability.
///   - Resolve: a land among the milled three is put into HAND; the rest stay
///     in the graveyard; no Treasure created.
///   - Resolve: a nonbasic land is eligible ("a land card", no Basic
///     restriction).
///   - Resolve: no land among the milled three → no card to hand, a Treasure
///     token is created.
///   - Resolve: empty library → no land milled → Treasure (clean, no throw).
/// </summary>
[Trait("Color", "G")]
public class PatientNaturalistFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ───────────────────────────────────────────────────────────────────
    // Identity
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void PatientNaturalist_IsHumanScout2_3_AtCost2G()
    {
        var card = PatientNaturalistFactory.Create(_alice);

        card.Name.Should().Be("Patient Naturalist");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        card.Power.Should().Be(2);
        card.Toughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PatientNaturalist_HasExactlyOneTriggeredAbility()
    {
        var card = PatientNaturalistFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one ETB trigger on Patient Naturalist.");
    }

    // ───────────────────────────────────────────────────────────────────
    // Resolve
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void EtbTrigger_LandAmongMilled_GoesToHand_RestStayInGraveyard()
    {
        // Top three: 2 nonland + 1 basic land. The land is recovered to hand;
        // the two nonland cards remain milled in the graveyard; no Treasure.
        var i1 = SeedLibraryCard(new Instant("Lightning Bolt", "{R}"));
        var i2 = SeedLibraryCard(new Sorcery("Doom Blade", "{1}{B}"));
        var forest = SeedLibraryCard(new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest }));

        ResolveEtb();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(forest, "the milled land card is put into hand");
        forest.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Graveyard.GetCards()
            .Should().Contain(new Card[] { i1, i2 }, "the milled nonland cards stay in the graveyard");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(forest);
        TreasureCount(_alice).Should().Be(0, "a land was found → no Treasure fallback");
    }

    [Fact]
    public void EtbTrigger_NonbasicLand_IsEligible()
    {
        // Oracle says "a land card" with no Basic-supertype restriction — a
        // nonbasic land is eligible.
        var dual = SeedLibraryCard(new Land("Stomping Ground",
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Forest }));

        ResolveEtb();

        _alice.Zones.Hand.GetCards().Should().Contain(dual,
            "a nonbasic land is still 'a land card'.");
        dual.Zone.Should().Be(ZoneType.Hand);
        TreasureCount(_alice).Should().Be(0);
    }

    [Fact]
    public void EtbTrigger_NoLandAmongMilled_CreatesTreasure()
    {
        var cards = new[] { "A", "B", "C" }
            .Select(n => SeedLibraryCard(new Instant(n, "")))
            .ToList();

        ResolveEtb();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no land among the milled three → nothing goes to hand");
        _alice.Zones.Graveyard.GetCards().Should().Contain(cards,
            "all three milled cards are in the graveyard");
        TreasureCount(_alice).Should().Be(1,
            "no land milled → the 'If you can't' clause creates a Treasure token");
    }

    [Fact]
    public void EtbTrigger_EmptyLibrary_CreatesTreasure_NoThrow()
    {
        var card = PatientNaturalistFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        Action act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow(
            "empty library → mill is a clean no-op (CR 104.3c); the land 'Put' " +
            "can't be followed so a Treasure is created.");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        TreasureCount(_alice).Should().Be(1,
            "no land could be milled from an empty library → Treasure fallback");
    }

    // ───────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────

    private void ResolveEtb()
    {
        var card = PatientNaturalistFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();
    }

    private static int TreasureCount(Player p) =>
        p.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Treasure));

    private T SeedLibraryCard<T>(T card) where T : Card
    {
        card.SetOwner(_alice);
        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }
}
