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
/// Unit tests for <see cref="IgneousPouncerFactory"/> (Hour of Devastation).
///
/// Covers:
/// - Identity ({4}{B}{R} Creature — Elemental 5/1).
/// - Haste keyword marker (CR 702.10).
/// - Swampcycling + Mountaincycling + Cycling keyword markers (CR 702.32d
///   typecycling surfaces BOTH typed names + generic).
/// - Two cycling activated abilities, each {2} mana + DiscardSelfCost.
/// - Swampcycling end-to-end: pays {2}, discards self, tutors a Swamp,
///   publishes <see cref="CardCycledEvent"/> on the bus.
/// - Mountaincycling end-to-end: tutors a Mountain (the same union
///   predicate also picks up Mountains).
/// - Cycling cost gate: DiscardSelfCost CanPay is hand-only.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
[Trait("Color", "M")]
public class IgneousPouncerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Land BasicLand(string name, CardSubtype subtype, Player owner)
    {
        var land = new Land(
            name,
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { subtype });
        land.SetOwner(owner);
        return land;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void IgneousPouncer_Identity_Elemental51()
    {
        var card = IgneousPouncerFactory.Create(_alice);

        card.Name.Should().Be("Igneous Pouncer");
        card.ManaCost.ToString().Should().Be("{4}{B}{R}");
        card.BasePower.Should().Be(5);
        card.BaseToughness.Should().Be(1);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32d
    // -----------------------------------------------------------------------

    [Fact]
    public void IgneousPouncer_BothCyclingAbilities_Have2GenericAndDiscardSelf()
    {
        var card = IgneousPouncerFactory.Create(_alice);
        var cyclers = card.Abilities.OfType<ActivatedAbility>().ToList();

        cyclers.Should().HaveCount(2);
        foreach (var cycling in cyclers)
        {
            cycling.Costs.Should().HaveCount(2,
                "typecycling = {2} + DiscardSelfCost");
            cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

            var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
            mana.Generic.Should().Be(2, "cycling {2} charges two generic");
        }
    }

    // -----------------------------------------------------------------------
    // Swampcycling end-to-end — tutors a Swamp, publishes CardCycledEvent
    // -----------------------------------------------------------------------

    [Fact]
    public void IgneousPouncer_Swampcycling_EndToEnd_TutorsSwampAndPublishesCardCycledEvent()
    {
        // Seed library with a Swamp + a non-land noise card. The Swamp is
        // the only Swamp-or-Mountain match, so it must be tutored.
        var swamp = BasicLand("Swamp", CardSubtype.Swamp, _alice);
        _alice.Zones.Library.AddCard(swamp);
        swamp.SetZone(ZoneType.Library);

        var noise = new Instant("Dark Ritual", "{B}");
        noise.SetOwner(_alice);
        _alice.Zones.Library.AddCard(noise);
        noise.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var pouncer = IgneousPouncerFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(pouncer);
        pouncer.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("2"));

        // The swampcycling ability is the one carrying the Swampcycling
        // keyword marker — but both abilities share the union predicate, so
        // either tutors the Swamp. Pick the first activated ability.
        var cycling = card_FirstCycler(pouncer);
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }

        pouncer.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(swamp,
            "Swampcycling tutors a Swamp-or-Mountain card (CR 702.32d)");
        _alice.Zones.Hand.GetCards().Should().NotContain(noise,
            "the tutor filters to Swamp/Mountain subtype only");
        swamp.Zone.Should().Be(ZoneType.Hand);

        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(pouncer);
        captured.Player.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mountaincycling end-to-end — the same union predicate tutors a
    // Mountain when no Swamp is present.
    // -----------------------------------------------------------------------

    [Fact]
    public void IgneousPouncer_Mountaincycling_EndToEnd_TutorsMountain()
    {
        var mountain = BasicLand("Mountain", CardSubtype.Mountain, _alice);
        _alice.Zones.Library.AddCard(mountain);
        mountain.SetZone(ZoneType.Library);

        var forest = BasicLand("Forest", CardSubtype.Forest, _alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var pouncer = IgneousPouncerFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(pouncer);
        pouncer.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("2"));

        // Second activated ability = mountaincycling (attached after
        // swampcycling). Either would tutor the same union, but exercise
        // the second leg explicitly.
        var cycling = card_SecondCycler(pouncer);
        foreach (var cost in cycling.Costs)
        {
            cost.Pay(_alice);
        }
        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(mountain,
            "Mountaincycling tutors a Swamp-or-Mountain card (CR 702.32d)");
        _alice.Zones.Hand.GetCards().Should().NotContain(forest,
            "Forest is neither a Swamp nor a Mountain");
        mountain.Zone.Should().Be(ZoneType.Hand);
    }

    // -----------------------------------------------------------------------
    // Cycling cost gate — DiscardSelfCost CanPay is hand-only
    // -----------------------------------------------------------------------

    [Fact]
    public void IgneousPouncer_Cycling_DiscardSelfCost_FromLibrary_CannotPay()
    {
        var card = IgneousPouncerFactory.Create(_alice);
        card.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(card);

        var cycling = card_FirstCycler(card);
        var discardCost = cycling.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "CR 702.32a — cycling activates only from hand");
    }

    private static ActivatedAbility card_FirstCycler(ICard card) =>
        card.Abilities.OfType<ActivatedAbility>().First();

    private static ActivatedAbility card_SecondCycler(ICard card) =>
        card.Abilities.OfType<ActivatedAbility>().ElementAt(1);
}
