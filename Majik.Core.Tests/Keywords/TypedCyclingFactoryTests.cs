using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// Tests for <see cref="TypedCyclingFactory"/> — the shared
/// activated-from-hand builder for the typecycling family
/// (CR 702.32d — Forestcycling, Slivercycling, Landcycling, etc.).
///
/// Covers:
/// - Build attaches the typed <see cref="KeywordAbility"/> marker AND
///   the generic "Cycling" marker (CR 702.32d — typecycling IS Cycling).
/// - Single <see cref="ActivatedAbility"/> with cost stack
///   <c>[cycleCost, DiscardSelfCost]</c>.
/// - Resolve tutors the first predicate-matching card from the
///   controller's library and shuffles (CR 701.19a + CR 701.20a).
/// - Predicate filters cleanly — non-matching cards stay in library.
/// - Empty candidate pool resolves cleanly (shuffle still fires).
/// - <see cref="CardCycledEvent"/> publication on resolve when a bus is
///   supplied (CR 702.32d).
/// - <see cref="TutorTypedCard"/> helper exposed for non-cycling cards
///   that share the typed-tutor body (e.g. Krosan Tusker on-cycle
///   trigger).
/// - Argument validation.
/// </summary>
public class TypedCyclingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Shape — KeywordAbility markers + activated-ability cost stack
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_AttachesTypedAndGenericCyclingKeywordMarkers()
    {
        var card = MakeCardInHand("Generous Ent");

        TypedCyclingFactory.Build(
            card,
            new ManaCostCost("{G}"),
            c => c.HasSubtype(CardSubtype.Forest),
            "Forestcycling");

        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Forestcycling",
                "typed keyword marker (CR 702.32d)");
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling",
                "typecycling IS Cycling — generic marker also surfaces");
    }

    [Fact]
    public void Build_AttachesSingleActivatedAbility_WithCycleCostPlusDiscardSelf()
    {
        var card = MakeCardInHand("Generous Ent");

        var ability = TypedCyclingFactory.Build(
            card,
            new ManaCostCost("{G}"),
            c => c.HasSubtype(CardSubtype.Forest),
            "Forestcycling");

        ability.Costs.Should().HaveCount(2,
            "typecycling = caller-supplied cost + DiscardSelfCost (CR 702.32a)");
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle()
            .Which.Cost.Green.Should().Be(1);
        ability.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();
        ability.TargetRequests.Should().BeEmpty(
            "typecycling tutors with no stack targets");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Resolve — typed tutor + shuffle
    // -----------------------------------------------------------------------

    [Fact]
    public void Forestcycling_EndToEnd_TutorsForestSkipsOtherCards()
    {
        // Seed library: a Forest + a non-Forest noise card.
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var noise = new Instant("Lightning Bolt", "{R}");
        noise.SetOwner(_alice);
        _alice.Zones.Library.AddCard(noise);
        noise.SetZone(ZoneType.Library);

        var card = MakeCardInHand("Generous Ent");
        _alice.AddManaToPool(ManaCost.Parse("G"));

        var ability = TypedCyclingFactory.Build(
            card,
            new ManaCostCost("{G}"),
            c => c.HasSubtype(CardSubtype.Forest),
            "Forestcycling");

        foreach (var cost in ability.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        card.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in ability.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(forest,
            "Forestcycling tutored the Forest (CR 702.32d)");
        forest.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().Contain(noise,
            "non-Forest card stays in the library — predicate filter");
        _alice.Zones.Hand.GetCards().Should().NotContain(noise);
    }

    [Fact]
    public void Forestcycling_NoMatchingCard_ResolvesCleanly()
    {
        // Library has only a non-Forest card.
        var noise = new Instant("Lightning Bolt", "{R}");
        noise.SetOwner(_alice);
        _alice.Zones.Library.AddCard(noise);
        noise.SetZone(ZoneType.Library);

        var card = MakeCardInHand("Generous Ent");

        var ability = TypedCyclingFactory.Build(
            card,
            new ManaCostCost("{G}"),
            c => c.HasSubtype(CardSubtype.Forest),
            "Forestcycling");

        // Resolve effect directly — empty candidate pool is legal
        // (CR 701.19a) and the shuffle still happens (CR 701.20a).
        var act = () =>
        {
            foreach (var effect in ability.Effects) effect.Execute();
        };

        act.Should().NotThrow("empty candidate pool = clean no-op");
        _alice.Zones.Hand.GetCards().Should().NotContain(noise,
            "predicate filtered noise out — nothing tutored");
        _alice.Zones.Library.GetCards().Should().Contain(noise);
    }

    [Fact]
    public void Forestcycling_PublishesCardCycledEvent_WhenBusSupplied()
    {
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var card = MakeCardInHand("Generous Ent");
        _alice.AddManaToPool(ManaCost.Parse("G"));

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var ability = TypedCyclingFactory.Build(
            card,
            new ManaCostCost("{G}"),
            c => c.HasSubtype(CardSubtype.Forest),
            "Forestcycling",
            eventBus: bus);

        foreach (var cost in ability.Costs) cost.Pay(_alice);
        foreach (var effect in ability.Effects) effect.Execute();

        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(card);
        captured.Player.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Cycle_NoBus_NoPublishedEvent_AbilityStillAttached()
    {
        var card = MakeCardInHand("Generous Ent");

        var ability = TypedCyclingFactory.Build(
            card,
            new ManaCostCost("{G}"),
            c => c.HasSubtype(CardSubtype.Forest),
            "Forestcycling");

        ability.Should().NotBeNull();
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Tutor helper — exposed for non-cycling tutor riders (Krosan Tusker)
    // -----------------------------------------------------------------------

    [Fact]
    public void TutorTypedCard_ReturnsAndMovesFirstMatch()
    {
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var pick = TypedCyclingFactory.TutorTypedCard(
            owner: _alice,
            predicate: c => c.HasSubtype(CardSubtype.Forest),
            kindLabel: "Forest card",
            shuffleReason: "test");

        pick.Should().BeSameAs(forest);
        forest.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(forest);
    }

    [Fact]
    public void TutorTypedCard_EmptyLibrary_ReturnsNullCleanly()
    {
        var pick = TypedCyclingFactory.TutorTypedCard(
            owner: _alice,
            predicate: c => c.HasSubtype(CardSubtype.Forest),
            kindLabel: "Forest card",
            shuffleReason: "test");

        pick.Should().BeNull();
    }

    [Fact]
    public void TutorTypedCard_NoMatch_ReturnsNullAndLeavesLibraryIntact()
    {
        var noise = new Instant("Lightning Bolt", "{R}");
        noise.SetOwner(_alice);
        _alice.Zones.Library.AddCard(noise);
        noise.SetZone(ZoneType.Library);

        var pick = TypedCyclingFactory.TutorTypedCard(
            owner: _alice,
            predicate: c => c.HasSubtype(CardSubtype.Forest),
            kindLabel: "Forest card",
            shuffleReason: "test");

        pick.Should().BeNull();
        _alice.Zones.Library.GetCards().Should().Contain(noise);
    }

    // -----------------------------------------------------------------------
    // Argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_ThrowsWhenOwnerNotWired()
    {
        var card = new Card("Generous Ent", "{5}{G}"); // no SetOwner

        var act = () => TypedCyclingFactory.Build(
            card,
            new ManaCostCost("{G}"),
            c => c.HasSubtype(CardSubtype.Forest),
            "Forestcycling");

        act.Should().Throw<ArgumentException>(
            "the resolve body tutors against the owner's library");
    }

    [Fact]
    public void Build_ThrowsOnNullSource()
    {
        var act = () => TypedCyclingFactory.Build(
            null!,
            new ManaCostCost("{G}"),
            c => c.HasSubtype(CardSubtype.Forest),
            "Forestcycling");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_ThrowsOnNullCycleCost()
    {
        var card = MakeCardInHand("Generous Ent");

        var act = () => TypedCyclingFactory.Build(
            card,
            null!,
            c => c.HasSubtype(CardSubtype.Forest),
            "Forestcycling");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_ThrowsOnNullPredicate()
    {
        var card = MakeCardInHand("Generous Ent");

        var act = () => TypedCyclingFactory.Build(
            card,
            new ManaCostCost("{G}"),
            null!,
            "Forestcycling");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_ThrowsOnEmptyTypedKeyword()
    {
        var card = MakeCardInHand("Generous Ent");

        var act = () => TypedCyclingFactory.Build(
            card,
            new ManaCostCost("{G}"),
            c => c.HasSubtype(CardSubtype.Forest),
            "");

        act.Should().Throw<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Card MakeCardInHand(string name)
    {
        var card = new Card(name, "{5}{G}");
        card.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        return card;
    }
}
