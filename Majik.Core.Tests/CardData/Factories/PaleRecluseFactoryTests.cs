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
/// Unit tests for <see cref="PaleRecluseFactory"/> (Future Sight).
///
/// Oracle text (Scryfall):
///   "Reach (This creature can block creatures with flying.)
///    Forestcycling {2}, plainscycling {2} ({2}, Discard this card:
///    Search your library for a Forest or Plains card, reveal it, put it
///    into your hand, then shuffle.)"
///
/// Covers:
/// - Identity ({4}{G}{W} Creature — Spider 4/5).
/// - Reach keyword marker (CR 702.17).
/// - Forestcycling {2} + Plainscycling {2} typed-cycling markers
///   (CR 702.32d) — two distinct activated abilities, both sharing the
///   generic "Cycling" marker.
/// - Forestcycling end-to-end: pays {2}, discards self, tutors a Forest
///   card to hand, publishes <see cref="CardCycledEvent"/>.
/// - Plainscycling end-to-end: pays {2}, discards self, tutors a Plains
///   card to hand, publishes <see cref="CardCycledEvent"/>.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
public class PaleRecluseFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PaleRecluse_Identity_Spider45()
    {
        var card = PaleRecluseFactory.Create(_alice);

        card.Name.Should().Be("Pale Recluse");
        card.ManaCost.ToString().Should().Be("{4}{G}{W}");
        card.BasePower.Should().Be(4);
        card.BaseToughness.Should().Be(5);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Spider).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PaleRecluse_HasReachKeyword()
    {
        var card = PaleRecluseFactory.Create(_alice);
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Reach");
    }

    [Fact]
    public void PaleRecluse_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Pale Recluse", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Pale Recluse");
        card.HasSubtype(CardSubtype.Spider).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Reach");
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Forestcycling");
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Plainscycling");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2,
            "Forestcycling + Plainscycling are two distinct activated abilities");
    }

    // -----------------------------------------------------------------------
    // Typed-cycling ability shapes — CR 702.32d
    // -----------------------------------------------------------------------

    [Fact]
    public void PaleRecluse_HasTwoCyclingAbilities_EachGenericTwoAndDiscardSelf()
    {
        var card = PaleRecluseFactory.Create(_alice);
        var cyclings = card.Abilities.OfType<ActivatedAbility>().ToList();

        cyclings.Should().HaveCount(2);
        foreach (var cycling in cyclings)
        {
            cycling.Costs.Should().HaveCount(2,
                "each typecycling = {2} + DiscardSelfCost");
            cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();
            var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
            mana.Generic.Should().Be(2, "each typecycling costs {2}");
        }
    }

    // -----------------------------------------------------------------------
    // Forestcycling end-to-end — tutors a Forest, publishes CardCycledEvent
    // -----------------------------------------------------------------------

    [Fact]
    public void PaleRecluse_Forestcycling_EndToEnd_TutorsForest()
    {
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var plains = new Land(
            "Plains",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        _alice.Zones.Library.AddCard(plains);
        plains.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var card = PaleRecluseFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("2"));

        // Forestcycling is the ability tied to the "Forestcycling" marker —
        // identify by the matching activated ability whose effect tutors a
        // Forest. Both abilities have identical cost shape, so locate by the
        // typed marker ordering: factory attaches Forestcycling first.
        var forestcycling = card.Abilities.OfType<ActivatedAbility>().First();
        foreach (var cost in forestcycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }

        card.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in forestcycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(forest,
            "Forestcycling tutors a Forest card (CR 702.32d)");
        _alice.Zones.Hand.GetCards().Should().NotContain(plains,
            "Forestcycling filters to Forest subtype only");
        forest.Zone.Should().Be(ZoneType.Hand);

        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(card);
        captured.Player.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Plainscycling end-to-end — tutors a Plains, publishes CardCycledEvent
    // -----------------------------------------------------------------------

    [Fact]
    public void PaleRecluse_Plainscycling_EndToEnd_TutorsPlains()
    {
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var plains = new Land(
            "Plains",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        _alice.Zones.Library.AddCard(plains);
        plains.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var card = PaleRecluseFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("2"));

        // Factory attaches Plainscycling second.
        var plainscycling = card.Abilities.OfType<ActivatedAbility>().Skip(1).First();
        foreach (var cost in plainscycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }

        card.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in plainscycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(plains,
            "Plainscycling tutors a Plains card (CR 702.32d)");
        _alice.Zones.Hand.GetCards().Should().NotContain(forest,
            "Plainscycling filters to Plains subtype only");
        plains.Zone.Should().Be(ZoneType.Hand);

        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(card);
    }
}
