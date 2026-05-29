using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LorienRevealedFactory"/>.
///
/// Card: Lórien Revealed — Sorcery {3}{U}{U}
/// (The Lord of the Rings: Tales of Middle-earth). Oracle text (Scryfall):
///   "Draw three cards.
///    Islandcycling {1} ({1}, Discard this card: Search your library for an
///    Island card, reveal it, put it into your hand, then shuffle.)"
///
/// Covers:
///   - Identity (name, Sorcery type, mana cost {3}{U}{U}, mana value 5, blue).
///   - NamedCardFactory dispatch returns a Sorcery.
///   - Resolve effect draws three cards from top of library (CR 121.1).
///   - Empty library mid-resolve flags the SBA-driven loss (CR 704.5b).
///   - Islandcycling {1} ability shape (ManaCostCost {1} + DiscardSelfCost,
///     CR 702.32d) with both "Islandcycling" + "Cycling" keyword markers.
///   - End-to-end Islandcycling: pays {1}, discards self, tutors an Island
///     card to hand, shuffles, publishes CardCycledEvent (CR 702.32d).
/// </summary>
public class LorienRevealedFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Card SeedLibraryCard(Player owner, string name)
    {
        var c = new Creature(name, "{0}", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void LorienRevealed_Identity()
    {
        var c = LorienRevealedFactory.Create(_alice);

        c.Name.Should().Be("Lórien Revealed");
        c.ManaCost.Should().Be("{3}{U}{U}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LorienRevealed_ManaValue_IsFive()
    {
        var c = LorienRevealedFactory.Create(_alice);

        // {3}{U}{U} = generic 3 + two blue pips = CMC 5
        c.ManaCostValue.TotalValue.Should().Be(5);
    }

    [Fact]
    public void LorienRevealed_IsBlue()
    {
        var c = LorienRevealedFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Blue,
            "Lórien Revealed has {U} pips so it is blue");
    }

    [Fact]
    public void LorienRevealed_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Lórien Revealed", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Lórien Revealed");
        c.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Resolve: draw three cards (CR 121.1)
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsThreeCardsFromTopOfLibrary()
    {
        var c1 = SeedLibraryCard(_alice, "Top1");
        var c2 = SeedLibraryCard(_alice, "Top2");
        var c3 = SeedLibraryCard(_alice, "Top3");
        SeedLibraryCard(_alice, "Top4"); // remains in library

        var effects = LorienRevealedFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(new[] { c1, c2, c3 });
        _alice.Zones.Library.GetCards().Should().HaveCount(1,
            "exactly three cards were drawn off the top");
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_EmptyLibrary_FlagsSbaLossOnFirstDraw()
    {
        var effects = LorienRevealedFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "empty library mid-draw flags the SBA-driven loss (CR 704.5b)");
    }

    [Fact]
    public void Resolve_TwoCardLibrary_DrawsTwo_FlagsSbaLossOnThirdDraw()
    {
        var a = SeedLibraryCard(_alice, "A");
        var b = SeedLibraryCard(_alice, "B");

        var effects = LorienRevealedFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(new[] { a, b });
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "the third draw came up empty — SBA flag is set (CR 704.5b)");
    }

    // -------------------------------------------------------------------------
    // Islandcycling {1} — CR 702.32d
    // -------------------------------------------------------------------------

    [Fact]
    public void LorienRevealed_HasIslandcyclingAndGenericCyclingMarkers()
    {
        var card = (Sorcery)NamedCardFactory.Create("Lórien Revealed", _alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Islandcycling",
                "typed keyword marker (CR 702.32d)");
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling",
                "typecycling IS Cycling — generic marker also surfaces");
    }

    [Fact]
    public void LorienRevealed_HasIslandcyclingActivatedAbility_WithGenericManaAndDiscardSelf()
    {
        var card = (Sorcery)NamedCardFactory.Create("Lórien Revealed", _alice);

        var cycling = card.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;
        cycling.Costs.Should().HaveCount(2, "Islandcycling = {1} + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(1, "Islandcycling cost is {1}");
        manaCost.Blue.Should().Be(0);
    }

    [Fact]
    public void Islandcycling_EndToEnd_PaysOneDiscardsSelfTutorsIslandShuffles()
    {
        // Seed library: an Island + a non-Island noise card.
        var island = new Land(
            "Island",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        _alice.Zones.Library.AddCard(island);
        island.SetZone(ZoneType.Library);

        var noise = new Instant("Lightning Bolt", "{R}");
        noise.SetOwner(_alice);
        _alice.Zones.Library.AddCard(noise);
        noise.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var card = LorienRevealedFactory.Create(_alice, bus);
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("1"));

        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        card.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(island,
            "Islandcycling tutored the Island (CR 702.32d)");
        island.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().Contain(noise,
            "non-Island card stays in the library — predicate filter");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(card);
    }
}
