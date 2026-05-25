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
/// Unit tests for <see cref="ExpeditionMapFactory"/>.
///
/// Expedition Map — Artifact {1}.
///   "{1}, {T}, Sacrifice Expedition Map: Search your library for a land
///    card, reveal it, put it into your hand, then shuffle."
///
/// Covers:
/// - Identity (Artifact, {1}) + NamedCardFactory dispatch.
/// - Single activated ability with three costs ({1}, Tap, Sacrifice) +
///   no target requests.
/// - Resolve: sacrifices the map AND moves a land from library to hand
///   (deterministic first-land fallback when no agent registered).
/// - Resolve: non-land cards stay in library; only the picked land moves.
/// - Resolve with empty land pile (only non-lands present): still
///   sacrifices, no card moved.
/// </summary>
public class ExpeditionMapTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void ExpeditionMap_IsArtifact_OneCost()
    {
        var map = ExpeditionMapFactory.Create(_alice);

        map.Name.Should().Be("Expedition Map");
        map.HasType(CardType.Artifact).Should().BeTrue();
        map.ManaCost.Should().Be("{1}");
        map.Owner.Should().BeSameAs(_alice);
        map.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ExpeditionMap()
    {
        var card = NamedCardFactory.Create("Expedition Map", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Expedition Map");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{1}");
    }

    // --------------------------------------------------------------
    // Ability shape
    // --------------------------------------------------------------

    [Fact]
    public void ExpeditionMap_HasOneActivatedAbility_NoManaAbilities()
    {
        var map = ExpeditionMapFactory.Create(_alice);

        map.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        map.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void TutorAbility_Has_OneMana_Tap_AndSacrifice_NoTargets()
    {
        var map = ExpeditionMapFactory.Create(_alice);
        var tutor = map.Abilities.OfType<ActivatedAbility>().Single();

        tutor.TargetRequests.Should().BeEmpty();

        tutor.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("1"),
                "tutor costs {1}");
        tutor.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "tutor taps the map");
        tutor.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "tutor sacrifices the map");
    }

    // --------------------------------------------------------------
    // Resolve — picks a land, moves to hand, sacrifices map
    // --------------------------------------------------------------

    [Fact]
    public void Activate_Tutor_MovesLandToHand_AndSacrificesMap()
    {
        var forest = new Land("Forest",
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var bolt = new Card("Lightning Bolt", "{R}");
        bolt.AddCardType(CardType.Instant);
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var map = ExpeditionMapFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(map);
        map.SetZone(ZoneType.Battlefield);

        var tutor = map.Abilities.OfType<ActivatedAbility>().Single();
        tutor.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(forest, "the land was tutored to hand");
        _alice.Zones.Hand.GetCards().Should().NotContain(bolt, "non-land stays in library");
        _alice.Zones.Library.GetCards().Should().Contain(bolt);
        _alice.Zones.Library.GetCards().Should().NotContain(forest);
        forest.Zone.Should().Be(ZoneType.Hand);

        _alice.Zones.Graveyard.GetCards().Should().Contain(map);
        map.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Tutor_NoLandsInLibrary_StillSacrifices_NoCardMoved()
    {
        var bolt = new Card("Lightning Bolt", "{R}");
        bolt.AddCardType(CardType.Instant);
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var map = ExpeditionMapFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(map);
        map.SetZone(ZoneType.Battlefield);

        var tutor = map.Abilities.OfType<ActivatedAbility>().Single();
        tutor.Resolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no land candidate; CR 701.19a allows declining to find");
        _alice.Zones.Library.GetCards().Should().Contain(bolt);
        bolt.Zone.Should().Be(ZoneType.Library);

        // Map still sacrificed — the cost was paid.
        _alice.Zones.Graveyard.GetCards().Should().Contain(map);
        map.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Tutor_FindsAnyLand_BasicOrNonbasic()
    {
        // Tron tutor target — confirms HasType(Land) matches nonbasic
        // Urza-style lands, not just basics.
        var urzaTower = new Land("Urza's Tower");
        urzaTower.SetOwner(_alice);
        _alice.Zones.Library.AddCard(urzaTower);
        urzaTower.SetZone(ZoneType.Library);

        var map = ExpeditionMapFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(map);
        map.SetZone(ZoneType.Battlefield);

        var tutor = map.Abilities.OfType<ActivatedAbility>().Single();
        tutor.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(urzaTower);
        urzaTower.Zone.Should().Be(ZoneType.Hand);
    }
}
