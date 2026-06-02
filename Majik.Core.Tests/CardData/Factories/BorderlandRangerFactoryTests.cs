using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BorderlandRangerFactory"/> — Creature — Human Scout
/// Ranger {2}{G} 2/2 (Magic 2010 / reprints). Oracle:
///   "When this creature enters, you may search your library for a basic land
///    card, reveal it, put it into your hand, then shuffle."
///
/// Covers:
///   - Card identity (Creature + Human/Scout/Ranger, {2}{G}, 2/2, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shape: exactly one ETB <see cref="TriggeredAbility"/>, no
///     activated/mana abilities, no target requests.
///   - ETB resolve: tutors ONE basic land into the controller's hand (CR 603.6a).
///   - ETB resolve: only nonbasics in library → no card moved.
/// </summary>
[Trait("Color", "G")]
public class BorderlandRangerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BorderlandRanger_IsHumanScoutRanger_AtTwoG_TwoTwo()
    {
        var c = BorderlandRangerFactory.Create(_alice);

        c.Name.Should().Be("Borderland Ranger");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        c.HasSubtype(CardSubtype.Ranger).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void BorderlandRanger_HasOneEtbTrigger_NoActivatedOrManaAbilities()
    {
        var c = BorderlandRangerFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void EtbTrigger_HasNoTargetRequests()
    {
        var c = BorderlandRangerFactory.Create(_alice);

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        etb.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void Etb_Tutors_OneBasicIntoHand()
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

        var ranger = BorderlandRangerFactory.Create(_alice);
        var etb = ranger.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        var hand = _alice.Zones.Hand.GetCards().ToList();
        hand.Count.Should().Be(startHand + 1,
            "Borderland Ranger searches for A (one) basic land and puts it into hand");
        hand.OfType<Land>().Single().Zone.Should().Be(ZoneType.Hand);
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

        var ranger = BorderlandRangerFactory.Create(_alice);
        var etb = ranger.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startHand,
            "no basic land in library → nothing put into hand");
        _alice.Zones.Library.GetCards().Should().Contain(bog);
    }
}
