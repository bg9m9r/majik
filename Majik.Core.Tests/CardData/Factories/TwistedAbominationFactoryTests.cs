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
/// Unit tests for <see cref="TwistedAbominationFactory"/> (Torment).
///
/// Covers:
/// - Identity ({5}{B} Creature — Zombie Mutant 5/3).
/// - Swampcycling + Cycling keyword markers (CR 702.32d typecycling
///   surfaces BOTH typed + generic).
/// - Cycling activated ability shape ({2} mana + DiscardSelfCost).
/// - Swampcycling end-to-end: pays {2}, discards self, tutors a Swamp
///   to hand, leaves non-Swamp lands in the library, publishes
///   <see cref="CardCycledEvent"/> on the bus.
/// - Cycling cost gate: DiscardSelfCost CanPay is hand-only.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
[Trait("Color", "B")]
public class TwistedAbominationFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void TwistedAbomination_Identity_ZombieMutant53()
    {
        var card = TwistedAbominationFactory.Create(_alice);

        card.Name.Should().Be("Twisted Abomination");
        card.ManaCost.ToString().Should().Be("{5}{B}");
        card.BasePower.Should().Be(5);
        card.BaseToughness.Should().Be(3);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        card.HasSubtype(CardSubtype.Mutant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Swampcycling ability shape — CR 702.32d
    // -----------------------------------------------------------------------

    [Fact]
    public void TwistedAbomination_HasSwampcyclingActivatedAbility_With2GenericAndDiscardSelf()
    {
        var card = TwistedAbominationFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.Should().HaveCount(2, "swampcycling = {2} + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Generic.Should().Be(2, "swampcycling {2} charges two generic");
    }

    // -----------------------------------------------------------------------
    // Swampcycling end-to-end — pays {2}, discards, tutors Swamp,
    // publishes CardCycledEvent
    // -----------------------------------------------------------------------

    [Fact]
    public void TwistedAbomination_Swampcycling_EndToEnd_TutorsSwampAndPublishesCardCycledEvent()
    {
        // Seed library: a non-Swamp basic + a Swamp + a non-land card.
        // Swampcycling should tutor the Swamp, not the Forest or the
        // Instant.
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var swamp = new Land(
            "Swamp",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Swamp });
        swamp.SetOwner(_alice);
        _alice.Zones.Library.AddCard(swamp);
        swamp.SetZone(ZoneType.Library);

        var noise = new Instant("Dark Ritual", "{B}");
        noise.SetOwner(_alice);
        _alice.Zones.Library.AddCard(noise);
        noise.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var abomination = TwistedAbominationFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(abomination);
        abomination.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var cycling = abomination.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }

        abomination.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(swamp,
            "Swampcycling tutors a Swamp card (CR 702.32d)");
        _alice.Zones.Hand.GetCards().Should().NotContain(forest,
            "Swampcycling filters to Swamp subtype only");
        _alice.Zones.Hand.GetCards().Should().NotContain(noise,
            "Swampcycling filters to Swamp subtype only");
        swamp.Zone.Should().Be(ZoneType.Hand);

        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(abomination);
        captured.Player.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Cycling cost gate — DiscardSelfCost CanPay is hand-only
    // -----------------------------------------------------------------------

    [Fact]
    public void TwistedAbomination_Swampcycling_DiscardSelfCost_FromLibrary_CannotPay()
    {
        var card = TwistedAbominationFactory.Create(_alice);
        card.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(card);

        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = cycling.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "CR 702.32a — cycling activates only from hand");
    }
}
