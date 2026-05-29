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
/// Tests for <see cref="SolemnSimulacrumFactory"/> — Artifact Creature — Golem
/// {4} 2/2 (numerous reprints). Oracle:
///   "When this creature enters, you may search your library for a basic land
///    card, put that card onto the battlefield tapped, then shuffle.
///    When this creature dies, you may draw a card."
///
/// Covers:
///   - Card identity (Artifact + Creature + Golem, {4}, 2/2, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shape: exactly two <see cref="TriggeredAbility"/> (one ETB, one
///     dies), no activated/mana abilities, no target requests.
///   - ETB resolve: tutors ONE basic land to battlefield tapped (CR 701.18).
///   - ETB resolve: only nonbasics in library → no land moved.
///   - Dies resolve: controller draws a card (CR 121.1).
/// </summary>
public class SolemnSimulacrumTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SolemnSimulacrum_IsArtifactCreatureGolem_AtFour_TwoTwo()
    {
        var c = SolemnSimulacrumFactory.Create(_alice);

        c.Name.Should().Be("Solemn Simulacrum");
        c.ManaCost.Should().Be("{4}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Solemn Simulacrum is BOTH Artifact and Creature (CR 205.2a)");
        c.HasSubtype(CardSubtype.Golem).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SolemnSimulacrum()
    {
        var card = NamedCardFactory.Create("Solemn Simulacrum", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Solemn Simulacrum");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void Solemn_HasTwoTriggers_NoActivatedOrManaAbilities()
    {
        var c = SolemnSimulacrumFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void Triggers_HaveNoTargetRequests()
    {
        var c = SolemnSimulacrumFactory.Create(_alice);

        foreach (var trig in c.Abilities.OfType<TriggeredAbility>())
        {
            trig.TargetRequests.Should().BeEmpty();
        }
    }

    [Fact]
    public void Etb_Tutors_OneBasicToBattlefieldTapped()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        // A second basic so we exercise the "search for A basic" (singular)
        // path — only ONE should be moved.
        var island = new Land("Island",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        _alice.Zones.Library.AddCard(island);
        island.SetZone(ZoneType.Library);

        var solemn = SolemnSimulacrumFactory.Create(_alice);

        var etb = solemn.Abilities.OfType<TriggeredAbility>().Single(IsEtb);
        etb.Resolve();

        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Count(c => c is Land).Should().Be(1,
            "Solemn Simulacrum searches for A (one) basic land");
        var movedLand = battlefield.OfType<Land>().Single();
        movedLand.IsTapped.Should().BeTrue("the basic enters tapped (CR 701.18)");
        movedLand.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Library.GetCards().Should().Contain(c => c is Land,
            "only one of the two basics is taken");
    }

    [Fact]
    public void Etb_NoBasicsInLibrary_MovesNoLand()
    {
        var bog = new Land("Bojuka Bog"); // nonbasic
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var solemn = SolemnSimulacrumFactory.Create(_alice);
        var etb = solemn.Abilities.OfType<TriggeredAbility>().Single(IsEtb);
        etb.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().NotContain(bog);
        _alice.Zones.Library.GetCards().Should().Contain(bog);
    }

    [Fact]
    public void Dies_DrawsACard()
    {
        // Seed the library so a draw has something to take.
        var top = new Land("Plains",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Plains });
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var startHand = _alice.Zones.Hand.GetCards().Count();

        var solemn = SolemnSimulacrumFactory.Create(_alice);
        var dies = solemn.Abilities.OfType<TriggeredAbility>().Single(t => !IsEtb(t));
        dies.Resolve();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startHand + 1,
            "the dies trigger draws one card (CR 121.1)");
    }

    // The ETB trigger is the one active in Battlefield only; the dies trigger
    // is active in Battlefield + Graveyard. Disambiguate by active zones.
    private static bool IsEtb(TriggeredAbility t) =>
        !t.ActiveZones.Contains(ZoneType.Graveyard);
}
