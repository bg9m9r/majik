using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SatyrWayfinderFactory"/> — Creature — Satyr {1}{G}
/// 1/1 (Journey into Nyx). Oracle text (verified against Scryfall):
///   "When this creature enters, reveal the top four cards of your library.
///    You may put a land card from among them into your hand. Put the rest
///    into your graveyard."
///
/// Covers:
///   - Card identity (Creature, Satyr, 1/1, {1}{G}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Single ETB triggered ability.
///   - Resolve: a land in the top four goes to HAND, the rest go to the
///     GRAVEYARD.
///   - Resolve: a nonbasic land card is still eligible (oracle says "a land
///     card", not "a basic land card").
///   - Resolve: no land in the top four → nothing to hand, all four milled.
///   - Resolve: empty library → clean no-op.
/// </summary>
[Trait("Color", "G")]
public class SatyrWayfinderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ───────────────────────────────────────────────────────────────────
    // Identity / dispatch
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void SatyrWayfinder_IsSatyr1_1_AtCost1G()
    {
        var card = SatyrWayfinderFactory.Create(_alice);

        card.Name.Should().Be("Satyr Wayfinder");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Satyr).Should().BeTrue();
        card.Power.Should().Be(1);
        card.Toughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SatyrWayfinder()
    {
        var card = NamedCardFactory.Create("Satyr Wayfinder", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Satyr Wayfinder");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{G}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SatyrWayfinder_HasExactlyOneTriggeredAbility()
    {
        var card = SatyrWayfinderFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one ETB trigger on Satyr Wayfinder.");
    }

    // ───────────────────────────────────────────────────────────────────
    // Resolve
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void EtbTrigger_PutsLandToHand_RestToGraveyard()
    {
        // Top four: 3 nonland + 1 basic land. The land goes to hand; the
        // three nonland cards go to the graveyard.
        var i1 = SeedLibraryCard(new Instant("Lightning Bolt", "{R}"));
        var i2 = SeedLibraryCard(new Instant("Shock", "{R}"));
        var i3 = SeedLibraryCard(new Sorcery("Doom Blade", "{1}{B}"));
        var forest = SeedLibraryCard(new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest }));

        ResolveEtb();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(forest, "the land card is put into hand");
        forest.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Graveyard.GetCards()
            .Should().Contain(new Card[] { i1, i2, i3 }, "the rest go to graveyard");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(forest);
    }

    [Fact]
    public void EtbTrigger_NonbasicLand_IsEligible()
    {
        // Unlike Civic Wayfinder, Satyr Wayfinder says "a land card" with no
        // Basic-supertype restriction — a nonbasic land is eligible.
        var dual = SeedLibraryCard(new Land("Stomping Ground",
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Forest }));

        ResolveEtb();

        _alice.Zones.Hand.GetCards().Should().Contain(dual,
            "a nonbasic land is still 'a land card'.");
        dual.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void EtbTrigger_NoLandInTopFour_NothingToHand_AllMilled()
    {
        var cards = new[] { "A", "B", "C", "D" }
            .Select(n => SeedLibraryCard(new Instant(n, "")))
            .ToList();

        ResolveEtb();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no land among the top four → nothing goes to hand");
        _alice.Zones.Graveyard.GetCards().Should().Contain(cards,
            "with no land taken, all four revealed cards are put into the graveyard");
    }

    [Fact]
    public void EtbTrigger_EmptyLibrary_IsNoOp()
    {
        var card = SatyrWayfinderFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        Action act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow(
            "empty library → clean no-op (CR 701.21).");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // ───────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────

    private void ResolveEtb()
    {
        var card = SatyrWayfinderFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();
    }

    private T SeedLibraryCard<T>(T card) where T : Card
    {
        card.SetOwner(_alice);
        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }
}
