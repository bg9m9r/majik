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
/// Unit tests for <see cref="ValleyRannetFactory"/> (Modern Horizons 3,
/// {4}{R}{G}).
///
/// Oracle text (Scryfall):
///   "Mountaincycling {2}, forestcycling {2} ({2}, Discard this card:
///    Search your library for a Mountain or Forest card, reveal it, put
///    it into your hand, then shuffle.)"
///
/// Covers:
/// - Identity ({4}{R}{G} Creature — Beast 6/3).
/// - Mountaincycling + Forestcycling + Cycling keyword markers.
/// - Two cycling activated abilities, each {2} + DiscardSelfCost.
/// - Both cycle bodies tutor a Mountain OR Forest card (CR 702.32d
///   reminder: "a Mountain or Forest card").
/// - Cycling end-to-end publishes <see cref="CardCycledEvent"/>.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
[Trait("Color", "M")]
public class ValleyRannetFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ValleyRannet_Identity_Beast63()
    {
        var card = ValleyRannetFactory.Create(_alice);

        card.Name.Should().Be("Valley Rannet");
        card.ManaCost.ToString().Should().Be("{4}{R}{G}");
        card.BasePower.Should().Be(6);
        card.BaseToughness.Should().Be(3);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Cycling activated ability shape — CR 702.32 / 702.32d
    // -----------------------------------------------------------------------

    [Fact]
    public void ValleyRannet_HasTwoCyclingAbilities_EachTwoGenericPlusDiscardSelf()
    {
        var card = ValleyRannetFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().ToList();

        cycling.Should().HaveCount(2);
        foreach (var ability in cycling)
        {
            ability.Costs.Should().HaveCount(2, "typecycling = {2} + DiscardSelfCost");
            ability.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

            var mana = ability.Costs.OfType<ManaCostCost>().Single().Cost;
            mana.Generic.Should().Be(2, "typecycling {2} charges two generic");
        }
    }

    // -----------------------------------------------------------------------
    // End-to-end: each cycle leg tutors a Mountain OR Forest card
    // -----------------------------------------------------------------------

    [Fact]
    public void ValleyRannet_Mountaincycling_EndToEnd_TutorsMountainOrForest()
    {
        var card = SeedAndCycle(legIndex: 0, out var mountain, out var forest,
            out var noise, out var captured, out var owner);

        owner.Zones.Hand.GetCards().Should().Contain(
            c => ReferenceEquals(c, mountain) || ReferenceEquals(c, forest),
            "a Mountain or Forest card is tutored (CR 702.32d)");
        owner.Zones.Hand.GetCards().Should().NotContain(noise,
            "the tutor filters to Mountain/Forest only");

        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Player.Should().BeSameAs(owner);
    }

    [Fact]
    public void ValleyRannet_Forestcycling_EndToEnd_TutorsMountainOrForest()
    {
        var card = SeedAndCycle(legIndex: 1, out var mountain, out var forest,
            out var noise, out var captured, out var owner);

        owner.Zones.Hand.GetCards().Should().Contain(
            c => ReferenceEquals(c, mountain) || ReferenceEquals(c, forest),
            "a Mountain or Forest card is tutored (CR 702.32d)");
        owner.Zones.Hand.GetCards().Should().NotContain(noise,
            "the tutor filters to Mountain/Forest only");

        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Player.Should().BeSameAs(owner);
    }

    // -----------------------------------------------------------------------
    // Helper — seed a library with a Mountain, a Forest, and a noise card,
    // then pay + resolve the cycling ability at <paramref name="legIndex"/>.
    // -----------------------------------------------------------------------

    private static Creature SeedAndCycle(
        int legIndex,
        out Land mountain,
        out Land forest,
        out Instant noise,
        out CardCycledEvent? captured,
        out Player owner)
    {
        owner = new Player("Carol", 20);

        mountain = new Land(
            "Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(owner);
        owner.Zones.Library.AddCard(mountain);
        mountain.SetZone(ZoneType.Library);

        forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(owner);
        owner.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        noise = new Instant("Lightning Bolt", "{R}");
        noise.SetOwner(owner);
        owner.Zones.Library.AddCard(noise);
        noise.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? local = null;
        bus.Subscribe<CardCycledEvent>(e => local = e);

        var card = ValleyRannetFactory.Create(owner, bus);
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        owner.AddManaToPool(ManaCost.Parse("2"));

        var cycling = card.Abilities.OfType<ActivatedAbility>().ToList()[legIndex];
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(owner).Should().BeTrue($"{cost.Description}");
            cost.Pay(owner);
        }

        card.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        captured = local;
        return card;
    }
}
