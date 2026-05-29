using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="PilgrimsEyeFactory"/> — Artifact Creature — Thopter
/// {3} 1/1 (Conflux / many reprints). Oracle (verified against Scryfall):
///   "Flying
///    When this creature enters, you may search your library for a basic land
///    card, reveal it, put it into your hand, then shuffle."
///
/// Covers:
///   - Card identity (Artifact + Creature + Thopter, {3}, 1/1, owner / controller).
///   - Flying keyword marker (CR 702.9).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shape: exactly one ETB <see cref="TriggeredAbility"/>, no
///     activated/mana abilities, no target requests.
///   - ETB resolve: tutors ONE basic land to HAND (CR 701.19a), shuffles.
///   - ETB resolve: only nonbasics in library → no card moved to hand.
/// </summary>
public class PilgrimsEyeTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void PilgrimsEye_IsArtifactCreatureThopter_AtThree_OneOne()
    {
        var c = PilgrimsEyeFactory.Create(_alice);

        c.Name.Should().Be("Pilgrim's Eye");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Pilgrim's Eye is BOTH Artifact and Creature (CR 205.2a)");
        c.HasSubtype(CardSubtype.Thopter).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PilgrimsEye_HasFlying()
    {
        var c = PilgrimsEyeFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying",
                "Pilgrim's Eye has Flying (CR 702.9)");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PilgrimsEye()
    {
        var card = NamedCardFactory.Create("Pilgrim's Eye", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Pilgrim's Eye");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void PilgrimsEye_HasOneTrigger_NoActivatedOrManaAbilities()
    {
        var c = PilgrimsEyeFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void EtbTrigger_HasNoTargetRequests()
    {
        var c = PilgrimsEyeFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();

        etb.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void Etb_Tutors_OneBasicToHand()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        // A second basic so we exercise the "search for A basic" (singular)
        // path — only ONE should be moved to hand.
        var island = new Land("Island",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        _alice.Zones.Library.AddCard(island);
        island.SetZone(ZoneType.Library);

        var startHand = _alice.Zones.Hand.GetCards().Count();

        var eye = PilgrimsEyeFactory.Create(_alice);
        var etb = eye.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startHand + 1,
            "Pilgrim's Eye searches for A (one) basic land card and puts it into hand");
        var inHand = _alice.Zones.Hand.GetCards().OfType<Land>().Single();
        inHand.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().Contain(c => c is Land,
            "only one of the two basics is taken");
    }

    [Fact]
    public void Etb_NoBasicsInLibrary_MovesNoCard()
    {
        var bog = new Land("Bojuka Bog"); // nonbasic
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var startHand = _alice.Zones.Hand.GetCards().Count();

        var eye = PilgrimsEyeFactory.Create(_alice);
        var etb = eye.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startHand,
            "no basic land card exists to find, so nothing is put into hand");
        _alice.Zones.Library.GetCards().Should().Contain(bog);
    }
}
