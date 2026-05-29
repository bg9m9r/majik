using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="TravelersAmuletFactory"/>.
///
/// Traveler's Amulet — Artifact {1}.
///   "{1}, Sacrifice this artifact: Search your library for a basic land
///    card, reveal it, put it into your hand, then shuffle."
///
/// Covers:
/// - Identity (Artifact, {1}) + NamedCardFactory dispatch.
/// - Single activated ability with two costs ({1}, Sacrifice), NO Tap, and
///   no target requests (distinguishes it from Expedition Map's "{1}, {T}").
/// - Resolve: sacrifices the amulet AND moves a basic land library → hand
///   (deterministic first-basic fallback when no agent registered).
/// - Resolve: nonbasic land + non-land cards stay in library; basic-only
///   predicate (CR 305.6).
/// - Resolve with no basic land available: still sacrifices, no card moved
///   (CR 701.19a — declining to find is legal).
/// </summary>
public class TravelersAmuletTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void TravelersAmulet_IsArtifact_OneCost()
    {
        var amulet = TravelersAmuletFactory.Create(_alice);

        amulet.Name.Should().Be("Traveler's Amulet");
        amulet.HasType(CardType.Artifact).Should().BeTrue();
        amulet.ManaCost.Should().Be("{1}");
        amulet.Owner.Should().BeSameAs(_alice);
        amulet.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TravelersAmulet()
    {
        var card = NamedCardFactory.Create("Traveler's Amulet", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Traveler's Amulet");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{1}");
    }

    // --------------------------------------------------------------
    // Ability shape
    // --------------------------------------------------------------

    [Fact]
    public void TravelersAmulet_HasOneActivatedAbility_NoManaAbilities()
    {
        var amulet = TravelersAmuletFactory.Create(_alice);

        amulet.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        amulet.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void TutorAbility_Has_OneMana_AndSacrifice_NoTap_NoTargets()
    {
        var amulet = TravelersAmuletFactory.Create(_alice);
        var tutor = amulet.Abilities.OfType<ActivatedAbility>().Single();

        tutor.TargetRequests.Should().BeEmpty();

        tutor.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("1"),
                "tutor costs {1}");
        tutor.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "tutor sacrifices the amulet");
        tutor.Costs.OfType<AdditionalCost>()
            .Should().NotContain(c => c.CostType == AdditionalCostType.Tap,
                "Traveler's Amulet's printed cost has no {T} pip");
    }

    // --------------------------------------------------------------
    // Resolve — picks a basic land, moves to hand, sacrifices amulet
    // --------------------------------------------------------------

    [Fact]
    public void Activate_Tutor_MovesBasicLandToHand_AndSacrificesAmulet()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var bolt = new Card("Lightning Bolt", "{R}");
        bolt.AddCardType(CardType.Instant);
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var amulet = TravelersAmuletFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(amulet);
        amulet.SetZone(ZoneType.Battlefield);

        var tutor = amulet.Abilities.OfType<ActivatedAbility>().Single();
        tutor.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(forest, "the basic land was tutored to hand");
        _alice.Zones.Hand.GetCards().Should().NotContain(bolt, "non-land stays in library");
        _alice.Zones.Library.GetCards().Should().Contain(bolt);
        _alice.Zones.Library.GetCards().Should().NotContain(forest);
        forest.Zone.Should().Be(ZoneType.Hand);

        _alice.Zones.Graveyard.GetCards().Should().Contain(amulet,
            "the amulet was sacrificed as a cost");
        amulet.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Tutor_OnlyNonbasicLand_IsNotTutored_StillSacrifices()
    {
        // CR 305.6 — a nonbasic land is NOT a "basic land card"; the amulet
        // can only fetch basics (unlike Expedition Map, which fetches any land).
        var urzaTower = new Land("Urza's Tower");
        urzaTower.SetOwner(_alice);
        _alice.Zones.Library.AddCard(urzaTower);
        urzaTower.SetZone(ZoneType.Library);

        var amulet = TravelersAmuletFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(amulet);
        amulet.SetZone(ZoneType.Battlefield);

        var tutor = amulet.Abilities.OfType<ActivatedAbility>().Single();
        tutor.Resolve();

        _alice.Zones.Hand.GetCards().Should().NotContain(urzaTower,
            "a nonbasic land is not a basic land card");
        _alice.Zones.Library.GetCards().Should().Contain(urzaTower);
        urzaTower.Zone.Should().Be(ZoneType.Library);

        _alice.Zones.Graveyard.GetCards().Should().Contain(amulet);
        amulet.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Tutor_NoBasicsInLibrary_StillSacrifices_NoCardMoved()
    {
        var bolt = new Card("Lightning Bolt", "{R}");
        bolt.AddCardType(CardType.Instant);
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var amulet = TravelersAmuletFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(amulet);
        amulet.SetZone(ZoneType.Battlefield);

        var tutor = amulet.Abilities.OfType<ActivatedAbility>().Single();
        tutor.Resolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no basic land candidate; CR 701.19a allows declining to find");
        _alice.Zones.Library.GetCards().Should().Contain(bolt);
        bolt.Zone.Should().Be(ZoneType.Library);

        _alice.Zones.Graveyard.GetCards().Should().Contain(amulet);
        amulet.Zone.Should().Be(ZoneType.Graveyard);
    }
}
